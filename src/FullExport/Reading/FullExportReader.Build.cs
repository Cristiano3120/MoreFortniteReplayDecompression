using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Events;
using MoreFortniteReplayDecompression.Match;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Reading;

public sealed partial class FullExportReader
{
    /// <summary>
    /// Call after <c>ReadReplay</c>: links the players to the library's standard result (same id as <c>PlayerData.Id</c>),
    /// applies the match start and sorts by placement.
    /// </summary>
    public List<PlayerStats> BuildPlayers(FortniteReplay replay)
    {
        List<PlayerStats> players = [.. _stats.Values.Where(p => p.Id is not null)];
        Dictionary<int?, PlayerData> byId = replay.PlayerData.Where(d => d.Id is not null).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

        ChannelIndex? recorderChannel = replay.GameData.RecorderId is { } recorderId && _guidChannel.TryGetValue(new ChannelGuid(recorderId), out ChannelIndex channel)
            ? channel
            : null;

        foreach (PlayerStats? p in players)
        {
            p.Data = byId.GetValueOrDefault(p.Id);
            p.EpicId ??= p.Data?.EpicId;
            p.BotId ??= p.Data?.BotId;
            p.IsReplayOwner = p.Data?.IsReplayOwner == true || p.Channel == recorderChannel;

            if (p.IsReplayOwner)
            {
                p.EpicMatchStats = replay.Stats;
            }
        }

        float matchEnd = MatchEndTime(replay);
        StormPhases = Storm.BuildPhases(replay.MapData.SafeZones, WorldToReplayTime);
        SupplyDrops = BuildSupplyDrops(replay);

        AssignHits();
        AssignPawnData();
        AssignBuilds();
        AssignEliminations(replay, players);
        MarkFatalCueHits();
        AssignOverkill(players);
        AssignTeammates(replay.TeamData, players);

        FinishContext context = new(MatchStartTime, matchEnd, StormPhases, SupplyDrops);
        foreach (PlayerStats? p in players)
        {
            p.Finish(context);
        }

        players.Sort((a, b) => (a.Placement ?? int.MaxValue).CompareTo(b.Placement ?? int.MaxValue));
        return players;
    }

    /// <summary>Replay time of the end of the match; the end of the recording if the match end is unknown.</summary>
    private float MatchEndTime(FortniteReplay replay)
    {
        float recordingEnd = (replay.Info?.LengthInMs ?? 0) / 1000f;
        if (replay.GameData.MatchEndTime is { } worldEnd && worldEnd > 0 && _worldTimes.Count > 0)
        {
            return Math.Min(WorldToReplayTime(worldEnd), recordingEnd);
        }

        return recordingEnd;
    }

    /// <summary>
    /// Converts a world time (game clock, used by the safe zones and the game state) into a replay time,
    /// using the nearest replicated world time of the game state.
    /// </summary>
    private float WorldToReplayTime(double worldTime)
    {
        if (_worldTimes.Count == 0)
        {
            return (float)worldTime;
        }

        (float ReplayT, double WorldT) nearest = _worldTimes[0];
        foreach ((float ReplayT, double WorldT) sample in _worldTimes)
        {
            if (Math.Abs(sample.WorldT - worldTime) < Math.Abs(nearest.WorldT - worldTime))
            {
                nearest = sample;
            }
        }

        return (float)(nearest.ReplayT + (worldTime - nearest.WorldT));
    }

    private List<SupplyDropInfo> BuildSupplyDrops(FortniteReplay replay)
    {
        Dictionary<uint, FortniteReplayReader.Models.SupplyDrop> landed = replay.MapData.SupplyDrops
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.Last());

        List<SupplyDropInfo> drops = [];
        foreach ((float t, ChannelIndex channel, FVector? location) in _supplyDropSpawns)
        {
            FortniteReplayReader.Models.SupplyDrop? drop = landed.GetValueOrDefault(channel.Value);
            drops.Add(new SupplyDropInfo
            {
                SpawnT = t,
                SpawnLocation = location,
                LandingLocation = drop?.LandingLocation,
                Looted = drop?.Looted == true,
            });
        }

        return drops;
    }

    /// <summary>Moves everything that was collected per pawn (positions, knocks, weapons) to the pawn's player.</summary>
    private void AssignPawnData()
    {
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.Positions.Clear();
            stats.RawDownedChanges.Clear();
            stats.RawWeaponChanges.Clear();
            stats.RawDistance = 0;
        }

        foreach ((ChannelGuid pawn, ChannelGuid _) in _pawnPlayerStateGuid)
        {
            if (PlayerOfPawn(pawn) is not { } player)
            {
                continue;
            }

            if (_pawnPositions.TryGetValue(pawn, out List<PositionSample>? positions))
            {
                player.Positions.AddRange(positions);
            }

            if (_pawnDownedChanges.TryGetValue(pawn, out List<(float T, bool Downed)>? downed))
            {
                player.RawDownedChanges.AddRange(downed);
            }

            if (_pawnWeaponChanges.TryGetValue(pawn, out List<(float T, uint Weapon)>? weapons))
            {
                foreach ((float t, uint weapon) in weapons)
                {
                    player.RawWeaponChanges.Add(new WeaponChange(t, WeaponClass(new ChannelGuid(weapon))));
                }
            }

            player.RawDistance += _pawnDistance.GetValueOrDefault(pawn);
        }

        // A player can have several pawns (reboot), so the lists are sorted by time again.
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.Positions.Sort((a, b) => a.T.CompareTo(b.T));
            stats.RawDownedChanges.Sort((a, b) => a.T.CompareTo(b.T));
            stats.RawWeaponChanges.Sort((a, b) => a.T.CompareTo(b.T));
        }
    }

    /// <summary>
    /// Links the kill feed (knocks and eliminations) of the library to the players.
    /// Fortnite credits an elimination to the player who KNOCKED the victim, even if a teammate finished it;
    /// <see cref="EliminationEvent.CreditedTo"/> follows that rule (this matches Epic's end-of-match eliminations).
    /// </summary>
    private static void AssignEliminations(FortniteReplay replay, List<PlayerStats> players)
    {
        Dictionary<string, PlayerStats> byPlayerId = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerStats p in players)
        {
            p.RawEliminations.Clear();
            p.RawDeaths.Clear();

            if (p.Data?.PlayerId is { } playerId)
            {
                byPlayerId.TryAdd(playerId, p);
            }
        }

        // Victim -> (time, player) of the knock that is still open.
        Dictionary<PlayerStats, (float T, PlayerStats? Knocker)> openKnocks = [];

        foreach (PlayerElimination elimination in replay.Eliminations.OrderBy(x => x.Info.StartTime))
        {
            float t = elimination.Info.StartTime / 1000f;
            PlayerStats? eliminator = elimination.Eliminator is { } eliminatorId
                ? byPlayerId.GetValueOrDefault(eliminatorId)
                : null;
            PlayerStats? eliminated = elimination.Eliminated is { } eliminatedId
                ? byPlayerId.GetValueOrDefault(eliminatedId)
                : null;

            PlayerStats? creditedTo = eliminator;
            if (eliminated is not null)
            {
                if (elimination.Knocked)
                {
                    openKnocks[eliminated] = (t, eliminator);
                }
                else if (openKnocks.Remove(eliminated, out (float T, PlayerStats? Knocker) knock) && t - knock.T <= MaxKnockSeconds && !WasRevived(eliminated, knock.T, t))
                {
                    creditedTo = knock.Knocker ?? eliminator;
                }
            }

            EliminationEvent e = new()
            {
                T = t,
                Eliminator = eliminator,
                CreditedTo = creditedTo,
                Eliminated = eliminated,
                IsKnock = elimination.Knocked,
                IsSelfElimination = creditedTo is not null && creditedTo == eliminated,
                IsTeamElimination = creditedTo is not null && creditedTo != eliminated && creditedTo.TeamIndex is not null && creditedTo.TeamIndex == eliminated?.TeamIndex,
                GunType = elimination.GunType,
            };

            creditedTo?.RawEliminations.Add(e);
            eliminated?.RawDeaths.Add(e);
        }
    }

    private static void AssignTeammates(IEnumerable<TeamData> teamDatas, List<PlayerStats> players)
    {
        //TeamIndex -> list of player IDs
        Dictionary<int, List<int?>> playerIdToTeamIndex = teamDatas.ToDictionary(t => t.TeamIndex ?? -1, t => t.PlayerIds.ToList());
        foreach (PlayerStats player in players)
        {
            if (player.TeamIndex is { } teamIndex && playerIdToTeamIndex.TryGetValue(teamIndex, out List<int?>? teammateIds))
            {
                foreach (int? teammateId in teammateIds)
                {
                    PlayerStats? teammate = players.FirstOrDefault(p => p.Id == teammateId);
                    if (teammate is not null && teammate != player)
                    {
                        player.Teammates.Add(teammate);
                    }
                }
            }
        }
    }

    /// <summary><c>true</c> if <paramref name="victim"/> got up again between the knock and the elimination.</summary>
    private static bool WasRevived(PlayerStats victim, float knockT, float eliminationT)
    {
        return victim.RawDownedChanges.Any(c => !c.Downed && c.T > knockT && c.T < eliminationT - ReviveToleranceSeconds);
    }

    /// <summary>Pawn actor GUID -> player, using everything learned about the pawn during the whole replay.</summary>
    private PlayerStats? PlayerOfPawn(ChannelGuid? pawnGuid)
    {
        if (pawnGuid is not { } guid || guid.Value == 0)
        {
            return null;
        }

        if (!_pawnPlayerStateGuid.TryGetValue(guid, out ChannelGuid psGuid))
        {
            return null;
        }

        if (!_guidChannel.TryGetValue(psGuid, out ChannelIndex psChannel))
        {
            return null;
        }

        return _stats.GetValueOrDefault(psChannel);
    }

    private PlayerStats? PlayerOfPlayerState(ChannelGuid? playerStateGuid)
        => playerStateGuid is { } guid && _guidChannel.TryGetValue(guid, out ChannelIndex channel) ? _stats.GetValueOrDefault(channel) : null;

    private string? WeaponClass(ChannelGuid? weaponGuid)
        => weaponGuid is { } guid ? _guidArchetype.GetValueOrDefault(guid) : null;

    /// <summary>
    /// Turns the collected raw hits into <see cref="HitEvent"/>s. Done after reading because the pawn-to-player link
    /// (and the class of a weapon actor) are often only known after the hit was read, which includes the starter island.
    /// </summary>
    private void AssignHits()
    {
        _cueHitEvents.Clear();
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.RawDealt.Clear();
            stats.RawTaken.Clear();
            stats.RawStructure.Clear();
        }

        foreach (PendingHit pHit in _pendingHits)
        {
            PlayerStats? attacker = PlayerOfPawn(pHit.AttackerPawn) ?? PlayerOfPlayerState(pHit.AttackerPlayerState);
            PlayerStats? victim = PlayerOfPawn(pHit.Victim);

            if (victim is not null)
            {
                HitEvent hit = new()
                {
                    T = pHit.T,
                    Amount = pHit.Amount,
                    Attacker = attacker,
                    Victim = victim,
                    AttackerChannel = attacker?.Channel,
                    VictimChannel = victim.Channel,
                    IsCritical = pHit.Critical,
                    IsFatal = pHit.Fatal,
                    IsShield = pHit.Shield,
                    IsShieldDestroyed = pHit.ShieldDestroyed,
                    Location = pHit.Location,
                    Weapon = WeaponClass(pHit.WeaponGuid),
                    IsWeaponFromCue = pHit.WeaponFromCue,
                    SecondsSinceWeaponChange = pHit.SecondsSinceWeaponChange,
                    SecondsSinceVictimKnocked = pHit.VictimKnockTime is { } knockTime ? pHit.T - knockTime : null,
                    IsProjectileHit = pHit.AttackerPlayerState is not null,
                    IsSelfDamage = attacker is not null && attacker == victim,
                    IsTeamDamage = attacker is not null && attacker != victim && attacker.TeamIndex is not null && attacker.TeamIndex == victim.TeamIndex,
                };

                victim.RawTaken.Add(hit);
                attacker?.RawDealt.Add(hit);

                if (pHit.AttackerPlayerState is not null)
                {
                    _cueHitEvents.Add(hit);
                }
            }
            else if (attacker is not null && pHit.StructureAmount > 0)
            {
                attacker.RawStructure.Add((pHit.T, pHit.StructureAmount));
            }
        }
    }

    /// <summary>
    /// Hits that come from gameplay cues have no "fatal" flag. A hit that knocks is recognized while reading (the victim goes down
    /// right before the cue); a hit that eliminates directly is recognized here by the elimination of the victim at the same time.
    /// </summary>
    private void MarkFatalCueHits()
    {
        foreach (HitEvent hit in _cueHitEvents)
        {
            if (hit.IsFatal || hit.Victim is not { } victim)
            {
                continue;
            }

            hit.IsFatal = victim.RawDeaths.Any(e => Math.Abs(e.T - hit.T) < KnockCueDelaySeconds && e.CreditedTo == hit.Attacker);
        }
    }

    /// <summary>
    /// The health sample that counts as "before the hit" is at least this many seconds older than the hit. The cue of a fatal hit
    /// arrives up to <see cref="KnockCueDelaySeconds"/> after the victim's health already changed, so a newer sample could
    /// already show the state after the hit.
    /// </summary>
    private const float OverkillLookbackSeconds = 0.3f;

    /// <summary>
    /// Sets <see cref="HitEvent.Overkill"/> of every hit whose victim has health data (all players in a server replay, only the
    /// recording player in a client replay). A hit the victim survived has no overkill. For a fatal hit the health and shield
    /// the victim had left are taken from the last health sample before the hit, minus the other hits right before it that
    /// are not part of that sample yet.
    /// </summary>
    private static void AssignOverkill(List<PlayerStats> players)
    {
        foreach (PlayerStats victim in players)
        {
            List<HealthSample> health = victim.HealthTimeline;
            if (health.Count == 0 || victim.RawTaken.Count == 0)
            {
                continue;
            }

            List<HitEvent> hits = [.. victim.RawTaken.OrderBy(h => h.T)];
            int sample = -1;

            for (int i = 0; i < hits.Count; i++)
            {
                HitEvent hit = hits[i];
                float before = hit.T - OverkillLookbackSeconds;

                // Both lists are sorted by time, so the sample index only moves forward.
                while (sample + 1 < health.Count && health[sample + 1].T < before)
                {
                    sample++;
                }

                if (sample < 0)
                {
                    continue;
                }

                if (!hit.IsFatal)
                {
                    hit.Overkill = 0;
                    continue;
                }

                float left = Math.Max(0, health[sample].Health) + Math.Max(0, health[sample].Shield);
                for (int j = i - 1; j >= 0 && hits[j].T >= before; j--)
                {
                    left -= hits[j].Amount;
                }

                hit.Overkill = Math.Clamp(hit.Amount - Math.Max(0, left), 0, hit.Amount);
            }
        }
    }
}

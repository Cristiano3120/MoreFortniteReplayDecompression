using System.Linq.Expressions;
using System.Reflection;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Events;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using Microsoft.Extensions.Logging;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// A <see cref="ReplayReader"/> that additionally
/// <list type="bullet">
/// <item>collects per-player statistics (<see cref="PlayerStats"/>): health, damage dealt and taken,</item>
/// <item>optionally records every read export group as a typed time series (<see cref="Events"/>).</item>
/// </list>
/// The library's standard result (<see cref="FortniteReplay"/>) is built unchanged in parallel.
/// Use one reader instance per replay: the internal state is not reset between replays.
/// </summary>
public sealed class FullExportReader : ReplayReader
{
    // Movement data of the pawn: stored at most once per interval; everything else (states, weapon, ...) is always stored.
    private static readonly HashSet<string> NoisyPawnProps =
    [
        nameof(PlayerPawn.ReplicatedMovement), nameof(PlayerPawn.Location), nameof(PlayerPawn.Rotation),
        nameof(PlayerPawn.LinearVelocity), nameof(PlayerPawn.ReplayLastTransformUpdateTimeStamp), nameof(PlayerPawn.Position),
        nameof(PlayerPawn.WorldLookDir), nameof(PlayerPawn.ForwardAlpha), nameof(PlayerPawn.RightAlpha), nameof(PlayerPawn.TurnDelta),
        nameof(PlayerPawn.SteerAlpha), nameof(PlayerPawn.AccelerationPack), nameof(PlayerPawn.AccelerationZPack),
        nameof(PlayerPawn.RemoteViewPitch), nameof(PlayerPawn.PackedReplicatedSlopeAngles), nameof(PlayerPawn.GravityScale),
    ];

    // The parser reuses ONE instance per group type and resets it on the next read.
    // A stored timeline entry therefore needs its own (shallow) copy.
    private static readonly MethodInfo CloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static INetFieldExportGroup Snapshot(INetFieldExportGroup group) => (INetFieldExportGroup)CloneMethod.Invoke(group, null)!;

    /// <summary>Pawn movement updates further apart than this (cm) are a teleport (spawn, reboot, ...) and do not count as traveled distance.</summary>
    private const double MaxMovementStep = 10_000;

    private readonly ExportSettings _settings;
    private readonly float _positionInterval;
    private readonly bool _collectTimeline;

    private readonly Dictionary<ChannelIndex, float> _lastPositionTime = new();
    private readonly Dictionary<ChannelIndex, Health> _lastHealth = new();
    private readonly Dictionary<ChannelIndex, Health> _maxHealth = new();

    // Actor GUID <-> channel. "Live" maps only contain currently open actors; channel numbers are reused during a replay.
    private readonly Dictionary<ChannelIndex, string?> _channelArchetype = new();
    private readonly Dictionary<ChannelGuid, string?> _guidArchetype = new();
    private readonly Dictionary<ChannelGuid, ChannelIndex> _guidChannel = new();
    private readonly Dictionary<ChannelGuid, ChannelIndex> _liveGuidChannel = new();
    private readonly Dictionary<ChannelIndex, ChannelGuid> _channelGuid = new();

    // Pawn state, keyed by the pawn's ACTOR GUID. A GUID identifies one actor for the whole replay,
    // channel numbers are reused and are therefore not suitable as a permanent key.
    private readonly Dictionary<ChannelGuid, ChannelGuid> _pawnPlayerStateGuid = new();   // pawn -> GUID of its PlayerState actor
    private readonly Dictionary<ChannelGuid, List<(float T, bool Downed)>> _pawnDownedChanges = new();
    private readonly Dictionary<ChannelGuid, WeaponState> _pawnWeapon = new();
    private readonly Dictionary<ChannelGuid, List<(float T, uint Weapon)>> _pawnWeaponChanges = new();
    private readonly Dictionary<ChannelGuid, List<PositionSample>> _pawnPositions = new();
    private readonly Dictionary<ChannelGuid, FVector> _pawnLastLocation = new();
    private readonly Dictionary<ChannelGuid, double> _pawnDistance = new();
    private readonly Dictionary<ChannelGuid, (bool Skydiving, bool Parachute)> _pawnInAir = new();

    // Match-wide data
    private readonly List<(float ReplayT, double WorldT)> _worldTimes = [];
    private readonly List<(float T, ChannelIndex Channel, FVector? Location)> _supplyDropSpawns = [];

    // Hits are only collected while reading. Which player a pawn belongs to is often learned later
    // (the pawn names its PlayerState with a delay), so hits are assigned to players in BuildPlayers.
    private readonly List<PendingHit> _pendingHits = [];

    /// <summary>A shot is delivered in the same frame as a weapon change at most this many seconds apart: it still belongs to the old weapon.</summary>
    private const float SameFrameSeconds = 0.02f;

    /// <summary>The cue of the hit that knocks a player arrives at most this many seconds after the player's <c>bIsDBNO</c>.</summary>
    private const float KnockCueDelaySeconds = 0.25f;

    /// <summary>A knocked player whose knock ended this many seconds before the elimination was revived in between.</summary>
    private const float ReviveToleranceSeconds = 0.5f;

    private sealed class WeaponState
    {
        public uint Current;
        public uint Previous;
        public float ChangedAt = float.NegativeInfinity;
    }

    private sealed record PendingHit(
        float T, ChannelGuid? AttackerPawn, ChannelGuid? Victim, float Amount, bool Critical, bool Fatal, bool Shield, bool ShieldDestroyed,
        FVector? Location, ChannelGuid? WeaponGuid, bool WeaponFromCue, float SecondsSinceWeaponChange, bool VictimDowned, float StructureAmount);

    private readonly Dictionary<ChannelIndex, PlayerStats> _stats = new();

    /// <summary>Typed timeline entries in replay order. Only filled when <c>collectTimeline</c> is set.</summary>
    public List<TimelineEvent> Events { get; } = [];

    /// <summary>All players found so far (key: PlayerState channel). Call <see cref="BuildPlayers"/> after reading for the final list.</summary>
    public IReadOnlyDictionary<ChannelIndex, PlayerStats> Players => _stats;

    public IReadOnlyDictionary<ChannelGuid, ChannelIndex> GuidToChannel => _guidChannel;
    public IReadOnlyDictionary<ChannelGuid, string?> GuidToArchetype => _guidArchetype;

    /// <summary>
    /// Replay time (seconds) at which the Battle Bus starts. Before that the players are on the starter island;
    /// damage there is not counted. <c>null</c> if no bus was detected.
    /// </summary>
    public float? MatchStartTime { get; private set; }

    /// <summary>Storm phases with replay times. Filled by <see cref="BuildPlayers"/>.</summary>
    public List<StormPhase> StormPhases { get; private set; } = [];

    /// <summary>Supply drops of the match. Filled by <see cref="BuildPlayers"/>.</summary>
    public List<SupplyDropInfo> SupplyDrops { get; private set; } = [];

    /// <param name="positionInterval">Seconds between two stored pawn updates that only contain movement data.</param>
    /// <param name="logger">Optional logger, for example a <see cref="GroupLogger"/>.</param>
    /// <param name="collectTimeline"><c>false</c> = only collect <see cref="PlayerStats"/> (faster, less memory).</param>
    public FullExportReader(float positionInterval = 1f, ILogger? logger = null, bool collectTimeline = true)
        : this(new ExportSettings { PositionInterval = positionInterval, CollectTimeline = collectTimeline }, logger)
    {
    }

    /// <param name="settings">What should be read, see <see cref="ExportSettings"/>.</param>
    /// <param name="logger">Optional logger, for example a <see cref="GroupLogger"/>.</param>
    public FullExportReader(ExportSettings settings, ILogger? logger = null)
        : base(logger!, ParseMode.Full, settings.GetIgnoredTypes())
    {
        _settings = settings;
        _positionInterval = settings.PositionInterval;
        _collectTimeline = settings.CollectTimeline;
    }

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
        AssignEliminations(replay, players);
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
                else if (openKnocks.Remove(eliminated, out (float T, PlayerStats? Knocker) knock) && !WasRevived(eliminated, knock.T, t))
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

    private string? WeaponClass(ChannelGuid? weaponGuid)
        => weaponGuid is { } guid ? _guidArchetype.GetValueOrDefault(guid) : null;

    /// <summary>
    /// Turns the collected raw hits into <see cref="HitEvent"/>s. Done after reading because the pawn-to-player link
    /// (and the class of a weapon actor) are often only known after the hit was read, which includes the starter island.
    /// </summary>
    private void AssignHits()
    {
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.RawDealt.Clear();
            stats.RawTaken.Clear();
            stats.RawStructure.Clear();
        }

        foreach (PendingHit pHit in _pendingHits)
        {
            PlayerStats? attacker = PlayerOfPawn(pHit.AttackerPawn);
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
                    VictimWasDowned = pHit.VictimDowned,
                    IsSelfDamage = attacker is not null && attacker == victim,
                    IsTeamDamage = attacker is not null && attacker != victim && attacker.TeamIndex is not null && attacker.TeamIndex == victim.TeamIndex,
                };

                victim.RawTaken.Add(hit);
                attacker?.RawDealt.Add(hit);
            }
            else if (attacker is not null && pHit.StructureAmount > 0)
            {
                attacker.RawStructure.Add((pHit.T, pHit.StructureAmount));
            }
        }
    }

    // ---- Callbacks of the base reader ------------------------------------------------------------------------------

    protected override void OnChannelOpened(uint uChannelIndex, NetworkGUID? actor)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnChannelOpened(channelIndex.Value, actor);

        if (_channelGuid.Remove(channelIndex, out ChannelGuid oldGuid))
        {
            _liveGuidChannel.Remove(oldGuid);
        }

        _lastPositionTime.Remove(channelIndex);
        _lastHealth.Remove(channelIndex);

        Actor? channelActor = Channels[channelIndex.Value]?.Actor;
        string? archetype = null;
        if (channelActor?.Archetype is { } arch && _netGuidCache.TryGetPathName(arch.Value, out var path))
        {
            archetype = path;
        }

        _channelArchetype[channelIndex] = archetype;

        if (archetype is not null && archetype.Contains("AthenaSupplyDrop_C", StringComparison.Ordinal))
        {
            _supplyDropSpawns.Add((CurrentTimeSeconds, channelIndex, channelActor?.Location));
        }

        if (actor is { } guid)
        {
            ChannelGuid cGuid = new(guid.Value);
            _liveGuidChannel[cGuid] = channelIndex;
            _channelGuid[channelIndex] = cGuid;
            _guidArchetype[cGuid] = archetype;
            _guidChannel[cGuid] = channelIndex;
        }

        if (_collectTimeline)
        {
            Events.Add(new ChannelOpenedEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                ActorGuid = actor?.Value,
                Archetype = archetype,
                Location = channelActor?.Location,
                Rotation = channelActor?.Rotation,
            });
        }
    }

    protected override void OnChannelClosed(uint uChannelIndex, NetworkGUID? actor)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnChannelClosed(channelIndex.Value, actor);

        if (_channelGuid.Remove(channelIndex, out ChannelGuid closedGuid))
        {
            _liveGuidChannel.Remove(closedGuid);
        }

        if (_collectTimeline)
        {
            Events.Add(new ChannelClosedEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                ActorGuid = actor?.Value,
                Archetype = _channelArchetype.GetValueOrDefault(channelIndex),
            });
        }
    }

    protected override void OnExportRead(uint uChannelIndex, INetFieldExportGroup? exportGroup)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnExportRead(channelIndex.Value, exportGroup);
        if (exportGroup is null)
        {
            return;
        }

        TrackPlayers(channelIndex, exportGroup);
        if (!_collectTimeline)
        {
            return;
        }

        // Keep only updates that actually carry data. Pawn updates that only contain movement are thinned out to the interval.
        bool keep = true;
        if (exportGroup is PlayerPawn)
        {
            bool due = !_lastPositionTime.TryGetValue(channelIndex, out var last) || CurrentTimeSeconds - last >= _positionInterval;
            if (due)
            {
                _lastPositionTime[channelIndex] = CurrentTimeSeconds;
            }

            keep = due || Presence.Any(exportGroup, NoisyPawnProps);
        }
        else
        {
            keep = Presence.Any(exportGroup, null);
        }

        if (keep)
        {
            Events.Add(new ExportEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                Type = exportGroup.GetType().Name,
                Group = Snapshot(exportGroup),
            });
        }
    }

    protected override void OnNetDeltaRead(uint uChannelIndex, NetDeltaUpdate update)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnNetDeltaRead(channelIndex.Value, update);
        if (!_collectTimeline || update.Export is null)
        {
            return;
        }

        Events.Add(new NetDeltaEvent
        {
            T = CurrentTimeSeconds,
            Channel = channelIndex,
            Type = update.Export.GetType().Name,
            ElementIndex = update.ElementIndex,
            Deleted = update.Deleted,
            Group = Snapshot(update.Export),
        });
    }

    // ---- Player assignment and statistics ---------------------------------------------------------------------------

    private PlayerStats Stats(ChannelIndex playerStateChannel)
    {
        if (!_stats.TryGetValue(playerStateChannel, out PlayerStats? s))
        {
            s = new PlayerStats
            {
                Channel = playerStateChannel
            };
            _stats[playerStateChannel] = s;
        }

        return s;
    }

    private void SetMatchStart(float t)
    {
        if (MatchStartTime is null || t < MatchStartTime)
        {
            MatchStartTime = t;
        }
    }

    private void TrackPlayers(ChannelIndex channelIndex, INetFieldExportGroup group)
    {
        switch (group)
        {
            case PlayerPawn pawn:
                if (!_channelGuid.TryGetValue(channelIndex, out ChannelGuid pawnGuid))
                {
                    break;
                }

                // The pawn names its PlayerState by GUID; this links the pawn to the player (resolved later, in BuildPlayers).
                if (pawn.PlayerState is { } psGuid && psGuid != 0)
                {
                    ChannelGuid playerStateGuid = new(psGuid);
                    _pawnPlayerStateGuid[pawnGuid] = playerStateGuid;
                }

                if (pawn.bIsDBNO is { } downed)
                {
                    TrackDowned(pawnGuid, downed);
                }

                if (pawn.CurrentWeapon is { } weapon && weapon != 0)
                {
                    if (!_pawnWeapon.TryGetValue(pawnGuid, out WeaponState? ws))
                    {
                        ws = new WeaponState();
                        _pawnWeapon[pawnGuid] = ws;
                    }

                    if (ws.Current != weapon)
                    {
                        ws.Previous = ws.Current;
                        ws.Current = weapon;
                        ws.ChangedAt = CurrentTimeSeconds;

                        if (_settings.TrackWeapons)
                        {
                            AddToList(_pawnWeaponChanges, pawnGuid, (CurrentTimeSeconds, weapon));
                        }
                    }
                }

                if (pawn.bIsSkydiving is not null || pawn.bIsParachuteOpen is not null)
                {
                    (bool Skydiving, bool Parachute) air = _pawnInAir.GetValueOrDefault(pawnGuid);
                    _pawnInAir[pawnGuid] = (pawn.bIsSkydiving ?? air.Skydiving, pawn.bIsParachuteOpen ?? air.Parachute);
                }

                if (_settings.TrackPositions && pawn.ReplicatedMovement?.Location is { } location)
                {
                    TrackPosition(pawnGuid, location);
                }

                break;

            case GameState gameState:
                if (gameState.ReplicatedWorldTimeSecondsDouble is { } worldTimeDouble)
                {
                    _worldTimes.Add((CurrentTimeSeconds, worldTimeDouble));
                }
                else if (gameState.ReplicatedWorldTimeSeconds is { } worldTime)
                {
                    _worldTimes.Add((CurrentTimeSeconds, worldTime));
                }
                break;

            case Aircraft aircraft:
                // FlightStartTime is set as soon as the Battle Bus takes off.
                if (aircraft.FlightStartTime > 0)
                {
                    SetMatchStart(CurrentTimeSeconds);
                }
                break;

            case FortPlayerState fp:
                if (fp.bInAircraft == true) // fallback if the Aircraft export is missing
                {
                    SetMatchStart(CurrentTimeSeconds);
                }

                PlayerStats stats = Stats(channelIndex);
                stats.Id ??= fp.PlayerId is null
                    ? fp.PlayerID
                    : (int?)fp.PlayerId;
                stats.EpicId ??= fp.UniqueId;
                stats.BotId ??= fp.BotUniqueId;

                if (fp.bIsABot is { } bot)
                {
                    stats.IsBot = bot;
                }

                if (fp.TeamIndex is { } team)
                {
                    stats.TeamIndex = team;
                }

                if (fp.Place is { } place)
                {
                    stats.Placement = place;
                }

                if (fp.KillScore is { } kills)
                {
                    stats.Kills = kills;
                }

                if (fp.TeamKillScore is { } teamKills)
                {
                    stats.TeamKills = teamKills;
                }
                break;

            case HealthSet health:
                TrackHealth(channelIndex, health);
                break;

            case BatchedDamageCues cue:
                TrackHit(channelIndex, cue);
                break;
        }
    }

    private static void AddToList<TKey, TValue>(Dictionary<TKey, List<TValue>> lists, TKey key, TValue value)
        where TKey : notnull
    {
        if (!lists.TryGetValue(key, out List<TValue>? list))
        {
            list = [];
            lists[key] = list;
        }

        list.Add(value);
    }

    /// <summary>Stores a knock / revive of a pawn (only real changes of <c>bIsDBNO</c>).</summary>
    private void TrackDowned(ChannelGuid pawnGuid, bool downed)
    {
        if (_pawnDownedChanges.TryGetValue(pawnGuid, out List<(float T, bool Downed)>? changes) && changes[^1].Downed == downed)
        {
            return;
        }

        AddToList(_pawnDownedChanges, pawnGuid, (CurrentTimeSeconds, downed));
    }

    /// <summary>
    /// <c>true</c> if the victim pawn was already knocked when the hit was fired.
    /// The damage cues are batched and arrive up to ~75 ms AFTER <c>bIsDBNO</c> (measured on the skin cup replays),
    /// so the fatal hit that knocks a player is not a hit on a downed player.
    /// </summary>
    private bool WasDownedBeforeHit(ChannelGuid pawnGuid, bool fatal)
    {
        if (!_pawnDownedChanges.TryGetValue(pawnGuid, out List<(float T, bool Downed)>? changes))
        {
            return false;
        }

        (float t, bool downed) = changes[^1];
        float sinceChange = CurrentTimeSeconds - t;

        if (downed && fatal && sinceChange < KnockCueDelaySeconds)
        {
            return false;
        }

        if (sinceChange >= SameFrameSeconds)
        {
            return downed;
        }

        return changes.Count > 1 && changes[^2].Downed;
    }

    /// <summary>
    /// Stores the pawn position (at most once per position interval) and sums up the traveled distance.
    /// Like Epic's "distance traveled" only the horizontal distance counts and skydiving / gliding is left out
    /// (checked on the skin cup replays: 36.8 km vs. Epic's 35.6 km over 11 matches; with height and skydiving it was 51 km).
    /// </summary>
    private void TrackPosition(ChannelGuid pawnGuid, FVector location)
    {
        (bool skydiving, bool parachute) = _pawnInAir.GetValueOrDefault(pawnGuid);
        bool inAir = skydiving || parachute;

        if (!inAir && MatchStartTime is { } start && CurrentTimeSeconds >= start && _pawnLastLocation.TryGetValue(pawnGuid, out FVector? last))
        {
            double dx = location.X - last.X;
            double dy = location.Y - last.Y;
            double step = Math.Sqrt((dx * dx) + (dy * dy));
            if (step < MaxMovementStep)
            {
                _pawnDistance[pawnGuid] = _pawnDistance.GetValueOrDefault(pawnGuid) + step;
            }
        }

        _pawnLastLocation[pawnGuid] = location;

        if (_pawnPositions.TryGetValue(pawnGuid, out List<PositionSample>? positions) && CurrentTimeSeconds - positions[^1].T < _positionInterval)
        {
            return;
        }

        AddToList(_pawnPositions, pawnGuid, new PositionSample(CurrentTimeSeconds, location));
    }

    /// <summary>
    /// Health history and derived damage. The HealthSet values are nullable: <c>null</c> = not sent in this update,
    /// then the previous value is kept. A value that was never sent is 0 (the default is not replicated).
    /// </summary>
    private void TrackHealth(ChannelIndex channelIndex, HealthSet hs)
    {
        bool hasHealth = hs.HealthCurrentValue is not null;
        bool hasShield = hs.ShieldCurrentValue is not null;
        if (!hasHealth && !hasShield && hs.HealthMaxValue is null && hs.ShieldMaxValue is null)
        {
            return;
        }

        _lastHealth.TryGetValue(channelIndex, out Health previousHealthState);
        float hp = hs.HealthCurrentValue ?? previousHealthState.Hp;
        float shield = hs.ShieldCurrentValue ?? previousHealthState.Shield;

        _lastHealth[channelIndex] = new Health(hp, shield);

        float maxHp = hs.HealthMaxValue ?? _maxHealth.GetValueOrDefault(channelIndex).Hp;
        float maxShield = hs.ShieldMaxValue ?? _maxHealth.GetValueOrDefault(channelIndex).Shield;

        _maxHealth[channelIndex] = new Health(maxHp, maxShield);

        PlayerStats stats = Stats(channelIndex);
        stats.SetMaxHealthAndShield(maxHp, maxShield);

        if (stats.HealthTimeline.Count == 0)
        {
            stats.HealthTimeline.Add(new HealthSample(CurrentTimeSeconds, hp, shield));
        }
        else if (stats.HealthTimeline[^1].Health != hp || stats.HealthTimeline[^1].Shield != shield)
        {
            stats.HealthTimeline.Add(new HealthSample(CurrentTimeSeconds, hp, shield));
        }

        // The replay has no single hits for this; damage is the decrease of health plus shield between two updates.
        float lost = (previousHealthState.Hp + previousHealthState.Shield) - (hp + shield);
        if (lost > 0 && (previousHealthState.Hp > 0 || previousHealthState.Shield > 0))
        {
            stats.RawHealthLoss.Add((CurrentTimeSeconds, lost));

            if (_collectTimeline)
            {
                Events.Add(new DerivedDamageEvent
                {
                    T = CurrentTimeSeconds,
                    Channel = channelIndex,
                    Amount = lost,
                    Health = previousHealthState.Hp - hp,
                    Shield = previousHealthState.Shield - shield,
                    HealthAfter = hp,
                    ShieldAfter = shield,
                });
            }
        }
    }

    private void TrackHit(ChannelIndex attackerPawnChannel, BatchedDamageCues cue)
    {
        // The RPC runs on the attacker's pawn; HitActor is the GUID of the target. Only raw data is stored here.
        _channelGuid.TryGetValue(attackerPawnChannel, out ChannelGuid attackerPawn);

        // Weapon: the pawn's weapon property is read BEFORE the hit RPC of the same frame. A hit that arrives in the frame
        // of a weapon change was therefore fired with the old weapon (verified on a replay: all such hits fit only the old weapon).
        ChannelGuid? weaponGuid = null;
        float sinceChange = float.PositiveInfinity;
        if (attackerPawn != 0 && _pawnWeapon.TryGetValue(attackerPawn, out WeaponState? ws))
        {
            sinceChange = CurrentTimeSeconds - ws.ChangedAt;
            weaponGuid = new ChannelGuid(sinceChange < SameFrameSeconds && ws.Previous != 0 ? ws.Previous : ws.Current);
        }

        // Newer replays name the weapon in the cue itself; that is exact and wins over the guess from the pawn.
        bool weaponFromCue = cue.SourceWeapon is > 0;
        if (weaponFromCue)
        {
            weaponGuid = new ChannelGuid(cue.SourceWeapon!.Value);
        }

        ChannelGuid? victim = cue.HitActor is > 0
            ? new ChannelGuid(cue.HitActor.Value)
            : null;

        float structureAmount = (cue.HitActor is > 0
            ? cue.Magnitude
            : null)
            ?? cue.NonPlayerMagnitude ?? 0;

        PendingHit pendingHit = new
        (
            T: CurrentTimeSeconds,
            AttackerPawn: attackerPawn == 0 ? null : attackerPawn,
            Victim: victim,
            Amount: cue.Magnitude ?? 0,
            Critical: cue.bIsCritical == true,
            Fatal: cue.bIsFatal == true,
            Shield: cue.bIsShield == true,
            ShieldDestroyed: cue.bIsShieldDestroyed == true,
            Location: cue.Location,
            WeaponGuid: weaponGuid,
            WeaponFromCue: weaponFromCue,
            SecondsSinceWeaponChange: float.IsPositiveInfinity(sinceChange) ? -1 : sinceChange,
            VictimDowned: victim is { } v && WasDownedBeforeHit(v, cue.bIsFatal == true),
            StructureAmount: structureAmount
        );
        _pendingHits.Add(pendingHit);
    }

    // ---- Presence check ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Tells whether an export group carries data: at least one property that is not <c>null</c> (reference and nullable types)
    /// or not <c>default</c> (other value types). The parser resets a reused instance, so this means "was sent in this update".
    /// Checks are compiled once per type.
    /// </summary>
    private static class Presence
    {
        private static readonly Dictionary<(Type, bool), Func<object, bool>> Cache = new();
        private static readonly MethodInfo ObjectEquals = typeof(object).GetMethod(nameof(Equals), [typeof(object)])!;

        public static bool Any(INetFieldExportGroup group, HashSet<string>? exclude)
        {
            (Type, bool) key = (group.GetType(), exclude is not null);
            if (!Cache.TryGetValue(key, out Func<object, bool>? check)) Cache[key] = check = Build(key.Item1, exclude);
            return check(group);
        }

        private static Func<object, bool> Build(Type type, HashSet<string>? exclude)
        {
            var parameter = Expression.Parameter(typeof(object), "group");
            var typed = Expression.Convert(parameter, type);
            Expression? body = null;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                if (exclude?.Contains(property.Name) == true) continue;

                var access = Expression.Property(typed, property);
                var propertyType = property.PropertyType;
                Expression test;

                if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
                {
                    test = Expression.NotEqual(access, Expression.Constant(null, propertyType));
                }
                else if (propertyType.IsPrimitive || propertyType.IsEnum)
                {
                    test = Expression.NotEqual(access, Expression.Default(propertyType));
                }
                else
                {
                    test = Expression.Not(Expression.Call(
                        Expression.Convert(access, typeof(object)), ObjectEquals, Expression.Convert(Expression.Default(propertyType), typeof(object))));
                }

                body = body is null ? test : Expression.OrElse(body, test);
            }

            return body is null ? _ => false : Expression.Lambda<Func<object, bool>>(body, parameter).Compile();
        }
    }
}

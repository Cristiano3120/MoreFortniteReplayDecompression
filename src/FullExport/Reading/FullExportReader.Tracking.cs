using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using MoreFortniteReplayDecompression.TimelineEvents;
using Unreal.Core.Contracts;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Reading;

public sealed partial class FullExportReader
{
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
                        TrackToolChange(pawnGuid, weapon);

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

                if (pawn.ReplicatedMovement?.Location is { } location)
                {
                    _pawnCurrentLocation[pawnGuid] = location;

                    if (_settings.TrackPositions)
                    {
                        TrackPosition(pawnGuid, location);
                    }
                }

                break;

            case PlayerBuild build:
                TrackBuild(channelIndex, build);
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
}

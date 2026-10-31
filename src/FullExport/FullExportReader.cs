using System.Linq.Expressions;
using System.Reflection;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using Microsoft.Extensions.Logging;
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

    private readonly float _positionInterval;
    private readonly bool _collectTimeline;

    private readonly Dictionary<uint, float> _lastPositionTime = new();
    private readonly Dictionary<uint, (float hp, float shield)> _lastHealth = new();
    private readonly Dictionary<uint, (float maxHp, float maxShield)> _maxHealth = new();

    // Actor GUID <-> channel. "Live" maps only contain currently open actors; channel numbers are reused during a replay.
    private readonly Dictionary<uint, string?> _channelArchetype = new();
    private readonly Dictionary<uint, string?> _guidArchetype = new();
    private readonly Dictionary<uint, uint> _guidChannel = new();
    private readonly Dictionary<uint, uint> _liveGuidChannel = new();
    private readonly Dictionary<uint, uint> _channelGuid = new();

    // Pawn state, keyed by the pawn's ACTOR GUID. A GUID identifies one actor for the whole replay,
    // channel numbers are reused and are therefore not suitable as a permanent key.
    private readonly Dictionary<uint, uint> _pawnPlayerStateGuid = new();   // pawn -> GUID of its PlayerState actor
    private readonly Dictionary<uint, bool> _pawnDowned = new();
    private readonly Dictionary<uint, WeaponState> _pawnWeapon = new();

    // Hits are only collected while reading. Which player a pawn belongs to is often learned later
    // (the pawn names its PlayerState with a delay), so hits are assigned to players in BuildPlayers.
    private readonly List<PendingHit> _pendingHits = [];

    /// <summary>A shot is delivered in the same frame as a weapon change at most this many seconds apart: it still belongs to the old weapon.</summary>
    private const float SameFrameSeconds = 0.02f;

    private sealed class WeaponState
    {
        public uint Current;
        public uint Previous;
        public float ChangedAt = float.NegativeInfinity;
    }

    private sealed record PendingHit(
        float T, uint? AttackerPawn, uint? Victim, float Amount, bool Critical, bool Fatal, bool Shield, bool ShieldDestroyed,
        FVector? Location, uint? WeaponGuid, float SecondsSinceWeaponChange, bool VictimDowned, float StructureAmount);

    private readonly Dictionary<uint, PlayerStats> _stats = new();

    /// <summary>Typed timeline entries in replay order. Only filled when <c>collectTimeline</c> is set.</summary>
    public List<TimelineEvent> Events { get; } = [];

    /// <summary>All players found so far (key: PlayerState channel). Call <see cref="BuildPlayers"/> after reading for the final list.</summary>
    public IReadOnlyDictionary<uint, PlayerStats> Players => _stats;

    public IReadOnlyDictionary<uint, uint> GuidToChannel => _guidChannel;
    public IReadOnlyDictionary<uint, string?> GuidToArchetype => _guidArchetype;

    /// <summary>
    /// Replay time (seconds) at which the Battle Bus starts. Before that the players are on the starter island;
    /// damage there is not counted. <c>null</c> if no bus was detected.
    /// </summary>
    public float? MatchStartTime { get; private set; }

    /// <param name="positionInterval">Seconds between two stored pawn updates that only contain movement data.</param>
    /// <param name="logger">Optional logger, for example a <see cref="GroupLogger"/>.</param>
    /// <param name="collectTimeline"><c>false</c> = only collect <see cref="PlayerStats"/> (faster, less memory).</param>
    public FullExportReader(float positionInterval = 1f, ILogger? logger = null, bool collectTimeline = true)
        : base(logger!, ParseMode.Full)
    {
        _positionInterval = positionInterval;
        _collectTimeline = collectTimeline;
    }

    /// <summary>
    /// Call after <c>ReadReplay</c>: links the players to the library's standard result (same id as <c>PlayerData.Id</c>),
    /// applies the match start and sorts by placement.
    /// </summary>
    public List<PlayerStats> BuildPlayers(FortniteReplay replay)
    {
        var players = _stats.Values.Where(p => p.Id is not null).ToList();
        var byId = replay.PlayerData.Where(d => d.Id is not null).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var p in players) p.Data = byId.GetValueOrDefault(p.Id);

        AssignHits();

        foreach (var p in players) p.Finish(MatchStartTime);

        players.Sort((a, b) => (a.Placement ?? int.MaxValue).CompareTo(b.Placement ?? int.MaxValue));
        return players;
    }

    /// <summary>Pawn actor GUID -> player, using everything learned about the pawn during the whole replay.</summary>
    private PlayerStats? PlayerOfPawn(uint? pawnGuid)
    {
        if (pawnGuid is not { } guid || guid == 0) return null;
        if (!_pawnPlayerStateGuid.TryGetValue(guid, out var psGuid)) return null;
        if (!_guidChannel.TryGetValue(psGuid, out var psChannel)) return null;
        return _stats.GetValueOrDefault(psChannel);
    }

    private string? WeaponClass(uint? weaponGuid) => weaponGuid is { } g ? _guidArchetype.GetValueOrDefault(g) : null;

    /// <summary>
    /// Turns the collected raw hits into <see cref="HitEvent"/>s. Done after reading because the pawn-to-player link
    /// (and the class of a weapon actor) are often only known after the hit was read, which includes the starter island.
    /// </summary>
    private void AssignHits()
    {
        foreach (var s in _stats.Values)
        {
            s.RawDealt.Clear();
            s.RawTaken.Clear();
            s.RawStructure.Clear();
        }

        foreach (var h in _pendingHits)
        {
            var attacker = PlayerOfPawn(h.AttackerPawn);
            var victim = PlayerOfPawn(h.Victim);

            if (victim is not null)
            {
                var hit = new HitEvent
                {
                    T = h.T, Amount = h.Amount, Attacker = attacker, Victim = victim,
                    AttackerChannel = attacker?.Channel, VictimChannel = victim.Channel,
                    IsCritical = h.Critical, IsFatal = h.Fatal, IsShield = h.Shield, IsShieldDestroyed = h.ShieldDestroyed,
                    Location = h.Location, Weapon = WeaponClass(h.WeaponGuid),
                    SecondsSinceWeaponChange = h.SecondsSinceWeaponChange,
                    VictimWasDowned = h.VictimDowned,
                    IsSelfDamage = attacker is not null && attacker == victim,
                    IsTeamDamage = attacker is not null && attacker != victim && attacker.TeamIndex is not null && attacker.TeamIndex == victim.TeamIndex,
                };

                victim.RawTaken.Add(hit);
                attacker?.RawDealt.Add(hit);
            }
            else if (attacker is not null && h.StructureAmount > 0)
            {
                attacker.RawStructure.Add((h.T, h.StructureAmount));
            }
        }
    }

    // ---- Callbacks of the base reader ------------------------------------------------------------------------------

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);

        if (_channelGuid.Remove(channelIndex, out var oldGuid)) _liveGuidChannel.Remove(oldGuid);
        _lastPositionTime.Remove(channelIndex);
        _lastHealth.Remove(channelIndex);

        var channelActor = Channels[channelIndex]?.Actor;
        string? archetype = null;
        if (channelActor?.Archetype is { } arch && _netGuidCache.TryGetPathName(arch.Value, out var path)) archetype = path;

        _channelArchetype[channelIndex] = archetype;

        if (actor is { } guid)
        {
            _liveGuidChannel[guid.Value] = channelIndex;
            _channelGuid[channelIndex] = guid.Value;
            _guidArchetype[guid.Value] = archetype;
            _guidChannel[guid.Value] = channelIndex;
        }

        if (_collectTimeline)
        {
            Events.Add(new ChannelOpenedEvent
            {
                T = CurrentTimeSeconds, Channel = channelIndex, ActorGuid = actor?.Value, Archetype = archetype,
                Location = channelActor?.Location, Rotation = channelActor?.Rotation,
            });
        }
    }

    protected override void OnChannelClosed(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelClosed(channelIndex, actor);

        if (_channelGuid.Remove(channelIndex, out var closedGuid)) _liveGuidChannel.Remove(closedGuid);

        if (_collectTimeline)
        {
            Events.Add(new ChannelClosedEvent
            {
                T = CurrentTimeSeconds, Channel = channelIndex, ActorGuid = actor?.Value,
                Archetype = _channelArchetype.GetValueOrDefault(channelIndex),
            });
        }
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        if (exportGroup is null) return;

        TrackPlayers(channelIndex, exportGroup);
        if (!_collectTimeline) return;

        // Keep only updates that actually carry data. Pawn updates that only contain movement are thinned out to the interval.
        var keep = true;
        if (exportGroup is PlayerPawn)
        {
            var due = !_lastPositionTime.TryGetValue(channelIndex, out var last) || CurrentTimeSeconds - last >= _positionInterval;
            if (due) _lastPositionTime[channelIndex] = CurrentTimeSeconds;
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
                T = CurrentTimeSeconds, Channel = channelIndex, Type = exportGroup.GetType().Name, Group = Snapshot(exportGroup),
            });
        }
    }

    protected override void OnNetDeltaRead(uint channelIndex, NetDeltaUpdate update)
    {
        base.OnNetDeltaRead(channelIndex, update);
        if (!_collectTimeline || update.Export is null) return;

        Events.Add(new NetDeltaEvent
        {
            T = CurrentTimeSeconds, Channel = channelIndex, Type = update.Export.GetType().Name,
            ElementIndex = update.ElementIndex, Deleted = update.Deleted, Group = Snapshot(update.Export),
        });
    }

    // ---- Player assignment and statistics ---------------------------------------------------------------------------

    private PlayerStats Stats(uint playerStateChannel)
    {
        if (!_stats.TryGetValue(playerStateChannel, out var s)) _stats[playerStateChannel] = s = new PlayerStats { Channel = playerStateChannel };
        return s;
    }

    private void SetMatchStart(float t)
    {
        if (MatchStartTime is null || t < MatchStartTime) MatchStartTime = t;
    }

    private void TrackPlayers(uint channel, INetFieldExportGroup group)
    {
        switch (group)
        {
            case PlayerPawn pawn:
                if (!_channelGuid.TryGetValue(channel, out var pawnGuid)) break;

                // The pawn names its PlayerState by GUID; this links the pawn to the player (resolved later, in BuildPlayers).
                if (pawn.PlayerState is { } psGuid && psGuid != 0) _pawnPlayerStateGuid[pawnGuid] = psGuid;
                if (pawn.bIsDBNO is { } downed) _pawnDowned[pawnGuid] = downed;

                if (pawn.CurrentWeapon is { } weapon && weapon != 0)
                {
                    if (!_pawnWeapon.TryGetValue(pawnGuid, out var ws)) _pawnWeapon[pawnGuid] = ws = new WeaponState();
                    if (ws.Current != weapon)
                    {
                        ws.Previous = ws.Current;
                        ws.Current = weapon;
                        ws.ChangedAt = CurrentTimeSeconds;
                    }
                }

                break;

            case Aircraft aircraft:
                // FlightStartTime is set as soon as the Battle Bus takes off.
                if (aircraft.FlightStartTime > 0) SetMatchStart(CurrentTimeSeconds);
                break;

            case FortPlayerState fp:
                if (fp.bInAircraft == true) SetMatchStart(CurrentTimeSeconds);   // fallback if the Aircraft export is missing

                var s = Stats(channel);
                s.Id ??= fp.PlayerId is null ? fp.PlayerID : (int?)fp.PlayerId;
                s.EpicId ??= fp.UniqueId ?? fp.UniqueID;
                s.BotId ??= fp.BotUniqueId;
                if (fp.bIsABot is { } bot) s.IsBot = bot;
                if (fp.TeamIndex is { } team) s.TeamIndex = team;
                if (fp.Place is { } place) s.Placement = place;
                if (fp.KillScore is { } kills) s.Kills = kills;
                if (fp.TeamKillScore is { } teamKills) s.TeamKills = teamKills;
                break;

            case HealthSet health:
                TrackHealth(channel, health);
                break;

            case BatchedDamageCues cue:
                TrackHit(channel, cue);
                break;
        }
    }

    /// <summary>
    /// Health history and derived damage. The HealthSet properties are not nullable, so "not sent" cannot be told apart from 0;
    /// a value counts as sent if it or the matching max value is greater than 0.
    /// </summary>
    private void TrackHealth(uint channel, HealthSet hs)
    {
        var hasHealth = hs.HealthMaxValue > 0 || hs.HealthCurrentValue > 0;
        var hasShield = hs.ShieldMaxValue > 0 || hs.ShieldCurrentValue > 0;
        if (!hasHealth && !hasShield) return;

        _lastHealth.TryGetValue(channel, out var prev);
        var hp = hasHealth ? hs.HealthCurrentValue : prev.hp;
        var shield = hasShield ? hs.ShieldCurrentValue : prev.shield;
        _lastHealth[channel] = (hp, shield);

        var maxHp = hs.HealthMaxValue > 0 ? hs.HealthMaxValue : _maxHealth.GetValueOrDefault(channel).maxHp;
        var maxShield = hs.ShieldMaxValue > 0 ? hs.ShieldMaxValue : _maxHealth.GetValueOrDefault(channel).maxShield;
        _maxHealth[channel] = (maxHp, maxShield);

        var s = Stats(channel);
        if (s.HealthTimeline.Count == 0)
        {
            s.SetMaxHealthAndShield(maxHp, maxShield);
            s.HealthTimeline.Add(new HealthSample(CurrentTimeSeconds, hp, shield));
        }
        else if (s.HealthTimeline[^1].Health != hp || s.HealthTimeline[^1].Shield != shield)
        {
            s.HealthTimeline.Add(new HealthSample(CurrentTimeSeconds, hp, shield));
        }

        // The replay has no single hits for this; damage is the decrease of health plus shield between two updates.
        var lost = (prev.hp + prev.shield) - (hp + shield);
        if (lost > 0 && (prev.hp > 0 || prev.shield > 0))
        {
            s.RawHealthLoss.Add((CurrentTimeSeconds, lost));

            if (_collectTimeline)
            {
                Events.Add(new DerivedDamageEvent
                {
                    T = CurrentTimeSeconds, Channel = channel, Amount = lost,
                    Health = prev.hp - hp, Shield = prev.shield - shield, HealthAfter = hp, ShieldAfter = shield,
                });
            }
        }
    }

    private void TrackHit(uint attackerPawnChannel, BatchedDamageCues cue)
    {
        // The RPC runs on the attacker's pawn; HitActor is the GUID of the target. Only raw data is stored here.
        _channelGuid.TryGetValue(attackerPawnChannel, out var attackerPawn);

        // Weapon: the pawn's weapon property is read BEFORE the hit RPC of the same frame. A hit that arrives in the frame
        // of a weapon change was therefore fired with the old weapon (verified on a replay: all such hits fit only the old weapon).
        uint? weaponGuid = null;
        var sinceChange = float.PositiveInfinity;
        if (attackerPawn != 0 && _pawnWeapon.TryGetValue(attackerPawn, out var ws))
        {
            sinceChange = CurrentTimeSeconds - ws.ChangedAt;
            weaponGuid = sinceChange < SameFrameSeconds && ws.Previous != 0 ? ws.Previous : ws.Current;
        }

        var victim = cue.HitActor is > 0 ? cue.HitActor : null;
        var structureAmount = (cue.HitActor is > 0 ? cue.Magnitude : null) ?? cue.NonPlayerMagnitude ?? 0;

        _pendingHits.Add(new PendingHit(
            CurrentTimeSeconds, attackerPawn == 0 ? null : attackerPawn, victim, cue.Magnitude ?? 0,
            cue.bIsCritical == true, cue.bIsFatal == true, cue.bIsShield == true, cue.bIsShieldDestroyed == true,
            cue.Location, weaponGuid, float.IsPositiveInfinity(sinceChange) ? -1 : sinceChange,
            victim is { } v && _pawnDowned.GetValueOrDefault(v), structureAmount));
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
            var key = (group.GetType(), exclude is not null);
            if (!Cache.TryGetValue(key, out var check)) Cache[key] = check = Build(key.Item1, exclude);
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

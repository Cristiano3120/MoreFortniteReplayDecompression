using System.Reflection;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using Microsoft.Extensions.Logging;
using MoreFortniteReplayDecompression.Export;
using MoreFortniteReplayDecompression.Logging;
using MoreFortniteReplayDecompression.Match;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using MoreFortniteReplayDecompression.TimelineEvents;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace MoreFortniteReplayDecompression.Reading;

/// <summary>
/// A <see cref="ReplayReader"/> that additionally
/// <list type="bullet">
/// <item>collects per-player statistics (<see cref="PlayerStats"/>): health, damage dealt and taken,</item>
/// <item>optionally records every read export group as a typed time series (<see cref="Events"/>).</item>
/// </list>
/// The library's standard result (<see cref="FortniteReplay"/>) is built unchanged in parallel.
/// Use one reader instance per replay: the internal state is not reset between replays.
/// </summary>
public sealed partial class FullExportReader : ReplayReader
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

    // Builds: open build channels, build actors already seen, the current location of every pawn and the new pieces.
    private readonly Dictionary<ChannelIndex, (BuildMaterial Material, string Piece, FVector? Location)> _buildChannels = new();
    private readonly HashSet<ChannelGuid> _seenBuilds = new();
    private readonly Dictionary<ChannelGuid, List<(float T, HeldTool Tool)>> _pawnToolChanges = new();
    private readonly Dictionary<ChannelGuid, FVector> _pawnCurrentLocation = new();
    private readonly List<PendingBuild> _pendingBuilds = [];

    /// <summary>A shot is delivered in the same frame as a weapon change at most this many seconds apart: it still belongs to the old weapon.</summary>
    private const float SameFrameSeconds = 0.02f;

    /// <summary>The cue of the hit that knocks a player arrives at most this many seconds after the player's <c>bIsDBNO</c>.</summary>
    private const float KnockCueDelaySeconds = 0.25f;

    /// <summary>
    /// A knocked player whose knock ended more than this many seconds before the elimination was revived in between.
    /// When a knocked player is finished, <c>bIsDBNO</c> turns off up to ~0.8 s before the kill feed entry (skin cup replays).
    /// </summary>
    private const float ReviveToleranceSeconds = 2f;

    /// <summary>A knock older than this (seconds) is not credited any more: the player was revived or rebooted while not relevant.</summary>
    private const float MaxKnockSeconds = 90f;

    private sealed class WeaponState
    {
        public uint Current;
        public uint Previous;
        public float ChangedAt = float.NegativeInfinity;
    }

    private sealed record PendingHit(
        float T, ChannelGuid? AttackerPawn, ChannelGuid? Victim, float Amount, bool Critical, bool Fatal, bool Shield, bool ShieldDestroyed,
        FVector? Location, ChannelGuid? WeaponGuid, bool WeaponFromCue, float SecondsSinceWeaponChange, float? VictimKnockTime, float StructureAmount);

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
}

using FortniteReplayReader.Models;
using MoreFortniteReplayDecompression.Match;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using MoreFortniteReplayDecompression.TimelineEvents;
using Unreal.Core.Contracts;

namespace MoreFortniteReplayDecompression.Export;

/// <summary>Complete result: the library's standard result plus the time series as typed objects.</summary>
public sealed class ReplayExport
{
    /// <summary>
    /// The library's standard result (PlayerData, GameData, MapData, Eliminations, KillFeed, TeamData, ...).
    /// <para>
    /// <see cref="PlayerData.Locations"/> is always empty here. The library would store every movement update of every pawn
    /// (millions of objects and hundreds of MB per replay). Use <see cref="PlayerStats.Positions"/> instead
    /// and set <see cref="ExportSettings.PositionInterval"/> to 0 to get every update.
    /// </para>
    /// </summary>
    public required FortniteReplay Replay { get; init; }

    /// <summary>Time, length, tournament, playlist and other small facts about the replay.</summary>
    public required ReplayMetadata Metadata { get; init; }

    /// <summary>
    /// All entries in replay order (ascending time). Empty if the timeline was not collected.
    /// <para>
    /// The entries are of different types. To get one type with IntelliSense use the properties below instead of <c>OfType</c>:
    /// <see cref="Exports"/>, <see cref="Movements"/>, <see cref="NetDeltas"/>, <see cref="ChannelsOpened"/>,
    /// <see cref="ChannelsClosed"/> and <see cref="Damage"/>.
    /// </para>
    /// </summary>
    public required List<TimelineEvent> Timeline { get; init; }

    /// <summary>Actor GUID to channel (last known). Useful to resolve references like <c>PlayerPawn.PlayerState</c>.</summary>
    public required Dictionary<ChannelGuid, ChannelIndex> GuidToChannel { get; init; }

    /// <summary>Actor GUID to archetype path (for example a weapon class).</summary>
    public required Dictionary<ChannelGuid, string?> GuidToArchetype { get; init; }

    /// <summary>
    /// Replay time (seconds) at which the Battle Bus starts. Damage before that (starter island) is not counted in <see cref="PlayerStats"/>.
    /// <c>null</c> if no bus was detected.
    /// </summary>
    public float? MatchStartTime { get; init; }

    /// <summary>Storm phases with replay times, see <see cref="Storm.SafeZoneAt"/>.</summary>
    public List<StormPhase> StormPhases { get; init; } = [];

    /// <summary>Supply drops of the match (spawn time, spawn and landing location).</summary>
    public List<SupplyDropInfo> SupplyDrops { get; init; } = [];

    /// <summary>The player who recorded the replay (has <see cref="PlayerStats.EpicMatchStats"/>), <c>null</c> if unknown.</summary>
    public PlayerStats? ReplayOwner => Players.FirstOrDefault(p => p.IsReplayOwner);

    /// <summary>All players with health history and damage dealt/taken, sorted by placement.</summary>
    public required List<PlayerStats> Players { get; init; }
    public required List<PlayerStats> Bots { get; init; }

    /// <summary>Finds a player by name (case-insensitive). Returns <c>null</c> if there is none.</summary>
    public PlayerStats? Player(string name) =>
        Players.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// All updates of one group class, for example <c>Of&lt;PlayerPawn&gt;()</c> or <c>Of&lt;HealthSet&gt;()</c>.
    /// For <c>PlayerPawn</c> the movement fields are always empty here; they are in <see cref="Movements"/>.
    /// </summary>
    public IEnumerable<(float T, ChannelIndex Channel, TGroup Data)> Of<TGroup>() where TGroup : class, INetFieldExportGroup =>
        Exports.Where(e => e.Group is TGroup).Select(e => (e.T, e.Channel, (TGroup)e.Group));

    /// <summary>All list-element updates of one class, for example <c>DeltasOf&lt;FortClientObservedStat&gt;()</c>.</summary>
    public IEnumerable<NetDeltaEvent> DeltasOf<TGroup>() where TGroup : class, INetFieldExportGroup =>
        NetDeltas.Where(e => e.Group is TGroup);

    /// <summary>All export group updates (every group class), see <see cref="Of{TGroup}"/> for one class.</summary>
    public IEnumerable<ExportEvent> Exports => Timeline.OfType<ExportEvent>();

    /// <summary>
    /// Movement of the pawns (position, rotation, velocity, view direction, ...), split off the <c>PlayerPawn</c> updates.
    /// Filter by <see cref="TimelineEvent.Channel"/> for one pawn. For per-player positions <see cref="PlayerStats.Positions"/> is easier.
    /// </summary>
    public IEnumerable<MovementEvent> Movements => Timeline.OfType<MovementEvent>();

    /// <summary>All list-element updates (inventory items, stat counters, ...), see <see cref="DeltasOf{TGroup}"/> for one class.</summary>
    public IEnumerable<NetDeltaEvent> NetDeltas => Timeline.OfType<NetDeltaEvent>();

    /// <summary>Actors that appeared, with class (archetype) and spawn transform.</summary>
    public IEnumerable<ChannelOpenedEvent> ChannelsOpened => Timeline.OfType<ChannelOpenedEvent>();

    /// <summary>Actors that were destroyed or went out of relevance.</summary>
    public IEnumerable<ChannelClosedEvent> ChannelsClosed => Timeline.OfType<ChannelClosedEvent>();

    /// <summary>All derived damage events (health/shield decrease), regardless of the match start.</summary>
    public IEnumerable<DerivedDamageEvent> Damage => Timeline.OfType<DerivedDamageEvent>();
}

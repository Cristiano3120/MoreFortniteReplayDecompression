using FortniteReplayReader.Models;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Contracts;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// Base of every timeline entry. All entries are time series samples, so each one carries a time and the channel it came from.
/// </summary>
public abstract record TimelineEvent
{
    /// <summary>
    /// Replay time in seconds since the start of the recording (the time of the demo frame the update was read in).
    /// This is not the in-game clock; the first frame is at about 0.
    /// </summary>
    public required float T { get; init; }

    /// <summary>
    /// Actor channel the update belongs to. Channel numbers are reused for different actors during a replay,
    /// so a channel only identifies an actor at a given time, never a player permanently.
    /// </summary>
    public required ChannelIndex Channel { get; init; }
}

/// <summary>One update of an export group (for example <c>PlayerPawn</c>, <c>HealthSet</c>, <c>SafeZoneIndicator</c>).</summary>
public sealed record ExportEvent : TimelineEvent
{
    /// <summary>Class name of <see cref="Group"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Snapshot of the group. Only properties that were sent in this update are set; the rest is <c>null</c> or <c>default</c>.</summary>
    public required INetFieldExportGroup Group { get; init; }
}

/// <summary>An update of a replicated list element (for example an inventory item or a stat counter).</summary>
public sealed record NetDeltaEvent : TimelineEvent
{
    public required string Type { get; init; }
    public required int ElementIndex { get; init; }
    public required bool Deleted { get; init; }
    public required INetFieldExportGroup Group { get; init; }
}

/// <summary>An actor appeared. Contains its class (archetype) and spawn transform.</summary>
public sealed record ChannelOpenedEvent : TimelineEvent
{
    public uint? ActorGuid { get; init; }
    public string? Archetype { get; init; }
    public FVector? Location { get; init; }
    public FRotator? Rotation { get; init; }
}

/// <summary>An actor was destroyed or went out of relevance. Its channel number can be reused afterwards.</summary>
public sealed record ChannelClosedEvent : TimelineEvent
{
    public uint? ActorGuid { get; init; }
    public string? Archetype { get; init; }
}

/// <summary>
/// Derived damage: decrease of health plus shield between two <c>HealthSet</c> updates of a player.
/// The replay does not say who caused it.
/// </summary>
public sealed record DerivedDamageEvent : TimelineEvent
{
    public required float Amount { get; init; }
    public required float Health { get; init; }
    public required float Shield { get; init; }
    public required float HealthAfter { get; init; }
    public required float ShieldAfter { get; init; }
}

/// <summary>Complete result: the library's standard result plus the time series as typed objects.</summary>
public sealed class ReplayExport
{
    /// <summary>The library's standard result (PlayerData, GameData, MapData, Eliminations, KillFeed, TeamData, ...).</summary>
    public required FortniteReplay Replay { get; init; }

    /// <summary>All entries in replay order (ascending time). Empty if the timeline was not collected.</summary>
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

    /// <summary>All updates of one group class, for example <c>Of&lt;PlayerPawn&gt;()</c> or <c>Of&lt;HealthSet&gt;()</c>.</summary>
    public IEnumerable<(float T, ChannelIndex Channel, TGroup Data)> Of<TGroup>() where TGroup : class, INetFieldExportGroup =>
        Timeline.OfType<ExportEvent>().Where(e => e.Group is TGroup).Select(e => (e.T, e.Channel, (TGroup)e.Group));

    /// <summary>All list-element updates of one class, for example <c>DeltasOf&lt;FortClientObservedStat&gt;()</c>.</summary>
    public IEnumerable<NetDeltaEvent> DeltasOf<TGroup>() where TGroup : class, INetFieldExportGroup =>
        Timeline.OfType<NetDeltaEvent>().Where(e => e.Group is TGroup);

    /// <summary>All derived damage events (health/shield decrease), regardless of the match start.</summary>
    public IEnumerable<DerivedDamageEvent> Damage => Timeline.OfType<DerivedDamageEvent>();
}

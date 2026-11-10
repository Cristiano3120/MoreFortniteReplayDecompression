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

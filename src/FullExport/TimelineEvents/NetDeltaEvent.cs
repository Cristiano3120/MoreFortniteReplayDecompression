using Unreal.Core.Contracts;

namespace MoreFortniteReplayDecompression.TimelineEvents;

/// <summary>An update of a replicated list element (for example an inventory item or a stat counter).</summary>
public sealed record NetDeltaEvent : TimelineEvent
{
    public required string Type { get; init; }
    public required int ElementIndex { get; init; }
    public required bool Deleted { get; init; }
    public required INetFieldExportGroup Group { get; init; }
}

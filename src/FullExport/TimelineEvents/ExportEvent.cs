using Unreal.Core.Contracts;

namespace MoreFortniteReplayDecompression.TimelineEvents;

/// <summary>One update of an export group (for example <c>PlayerPawn</c>, <c>HealthSet</c>, <c>SafeZoneIndicator</c>).</summary>
public sealed record ExportEvent : TimelineEvent
{
    /// <summary>Class name of <see cref="Group"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Snapshot of the group. Only properties that were sent in this update are set; the rest is <c>null</c> or <c>default</c>.</summary>
    public required INetFieldExportGroup Group { get; init; }
}

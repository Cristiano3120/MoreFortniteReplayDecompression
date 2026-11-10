
namespace MoreFortniteReplayDecompression.TimelineEvents;

/// <summary>An actor was destroyed or went out of relevance. Its channel number can be reused afterwards.</summary>
public sealed record ChannelClosedEvent : TimelineEvent
{
    public uint? ActorGuid { get; init; }
    public string? Archetype { get; init; }
}

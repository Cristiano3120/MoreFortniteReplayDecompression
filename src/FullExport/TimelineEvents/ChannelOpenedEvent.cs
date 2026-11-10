using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.TimelineEvents;

/// <summary>An actor appeared. Contains its class (archetype) and spawn transform.</summary>
public sealed record ChannelOpenedEvent : TimelineEvent
{
    public uint? ActorGuid { get; init; }
    public string? Archetype { get; init; }
    public FVector? Location { get; init; }
    public FRotator? Rotation { get; init; }
}

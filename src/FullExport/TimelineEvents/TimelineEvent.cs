using MoreFortniteReplayDecompression.StronglyTypedIds;

namespace MoreFortniteReplayDecompression.TimelineEvents;

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

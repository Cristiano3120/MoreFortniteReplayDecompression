
namespace MoreFortniteReplayDecompression.TimelineEvents;

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

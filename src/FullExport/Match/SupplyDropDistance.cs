
namespace MoreFortniteReplayDecompression.Match;

/// <summary>How far a player was away from a supply drop when it appeared.</summary>
/// <param name="Drop">The supply drop.</param>
/// <param name="Meters">Horizontal distance in meters between the player and the landing location (spawn location if the landing location is unknown).</param>
public readonly record struct SupplyDropDistance(SupplyDropInfo Drop, float Meters);

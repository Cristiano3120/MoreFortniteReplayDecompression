namespace MoreFortniteReplayDecompression;

/// <summary>The player switched to another weapon / item.</summary>
/// <param name="T">Replay time in seconds since the start of the recording.</param>
/// <param name="Weapon">Class (archetype path) of the equipped weapon actor, <c>null</c> if unknown.</param>
public readonly record struct WeaponChange(float T, string? Weapon);

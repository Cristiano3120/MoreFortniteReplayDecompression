namespace MoreFortniteReplayDecompression;

/// <summary>A health/shield state of a player. A new sample is only added when a value changes.</summary>
/// <param name="T">Replay time in seconds since the start of the recording (not the in-game clock).</param>
public readonly record struct HealthSample(float T, float Health, float Shield);

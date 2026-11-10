using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>Position of a player's pawn.</summary>
/// <param name="T">Replay time in seconds since the start of the recording (not the in-game clock).</param>
public readonly record struct PositionSample(float T, FVector Location);

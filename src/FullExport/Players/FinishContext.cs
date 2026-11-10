using MoreFortniteReplayDecompression.Match;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>Match-wide values a <see cref="PlayerStats"/> needs to compute its sums.</summary>
/// <param name="MatchStart">Replay time of the Battle Bus start, <c>null</c> if unknown (then everything counts).</param>
/// <param name="MatchEnd">Replay time of the end of the match (or of the recording).</param>
/// <param name="StormPhases">Storm phases of the match.</param>
/// <param name="SupplyDrops">Supply drops of the match.</param>
internal sealed record FinishContext(float? MatchStart, float MatchEnd, IReadOnlyList<StormPhase> StormPhases, IReadOnlyList<SupplyDropInfo> SupplyDrops);

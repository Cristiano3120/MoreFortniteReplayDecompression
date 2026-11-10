
namespace MoreFortniteReplayDecompression.Players;

/// <summary>Time span in which a player was knocked down (DBNO).</summary>
/// <param name="Start">Replay time of the knock.</param>
/// <param name="End">Replay time of the revive or the end of the pawn, <c>null</c> if the player was still down at the end.</param>
public readonly record struct DownedInterval(float Start, float? End)
{
    public bool Contains(float t)
    {
        return t >= Start && (End is null || t <= End);
    }
}

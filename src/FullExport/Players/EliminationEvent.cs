
namespace MoreFortniteReplayDecompression.Players;

/// <summary>A knock or an elimination from the kill feed of the replay.</summary>
public sealed class EliminationEvent
{
    /// <summary>Replay time in seconds since the start of the recording.</summary>
    public float T { get; init; }

    /// <summary>The player named in the kill feed (for an elimination: who finished the victim).</summary>
    public PlayerStats? Eliminator { get; init; }

    /// <summary>
    /// The player who gets the elimination: for a finished knocked player that is the player who knocked him (Fortnite's rule),
    /// otherwise <see cref="Eliminator"/>.
    /// </summary>
    public PlayerStats? CreditedTo { get; init; }

    public PlayerStats? Eliminated { get; init; }

    /// <summary><c>true</c>: the victim was knocked down (DBNO), <c>false</c>: the victim was eliminated.</summary>
    public bool IsKnock { get; init; }

    public bool IsSelfElimination { get; init; }

    /// <summary><see cref="CreditedTo"/> and victim are on the same team (different players).</summary>
    public bool IsTeamElimination { get; init; }

    /// <summary>Damage type / gun type id of the kill feed.</summary>
    public byte GunType { get; init; }
}

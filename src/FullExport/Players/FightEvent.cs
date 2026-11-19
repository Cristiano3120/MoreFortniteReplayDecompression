namespace MoreFortniteReplayDecompression.Players;

/// <summary>How a fight ended for the player it belongs to (see <see cref="FightEvent.Outcome"/>).</summary>
public enum FightOutcome
{
    /// <summary>Nobody of the two was knocked or eliminated by the other.</summary>
    Open,

    /// <summary>The player knocked or eliminated the opponent.</summary>
    Won,

    /// <summary>The opponent knocked or eliminated the player.</summary>
    Lost,
}

/// <summary>
/// A fight between this player and one opponent, from the view of this player (the opponent has the same fight the other way round).
/// <para>
/// The replay has no fights; a fight is built from the hits between the two players: it starts with the first hit (of either of
/// them) and goes on as long as they keep fighting. It ends when one of them is eliminated, or when the rules of
/// <see cref="FightRules"/> say they stopped (they moved apart, or nobody hit for too long). The next hit starts a new fight.
/// So "hit someone, he runs away, hit him again in the endgame" are two fights.
/// </para>
/// Only fights between enemies count (no team or self damage), and only after the match start. Like all stats this is complete for
/// the replay owner and the players near them in a client replay, and for everyone in a server replay.
/// </summary>
public sealed class FightEvent
{
    /// <summary>The other player of the fight.</summary>
    public required PlayerStats Opponent { get; init; }

    /// <summary>Replay time of the first hit of the fight (of either player).</summary>
    public float Start { get; init; }

    /// <summary>Replay time of the end: the last hit, or the knock or elimination if that came later.</summary>
    public float End { get; init; }

    /// <summary>Seconds from <see cref="Start"/> to <see cref="End"/>.</summary>
    public float Duration => End - Start;

    /// <summary>The fight started in the endgame (<see cref="FightRules.EndgameZone"/>), where only the time rule ends a fight.</summary>
    public bool IsEndgame { get; init; }

    /// <summary>Replay time of this player's first hit on the opponent in this fight, <c>null</c> if the player did not hit.</summary>
    public float? FirstHitDealt { get; init; }

    public int HitsDealt { get; init; }
    public int HitsTaken { get; init; }
    public float DamageDealt { get; init; }
    public float DamageTaken { get; init; }

    /// <summary>This player knocked the opponent in this fight.</summary>
    public bool KnockedOpponent { get; init; }

    /// <summary>The opponent's elimination in this fight is credited to this player (also when a teammate finished the knocked opponent).</summary>
    public bool EliminatedOpponent { get; init; }

    /// <summary>The opponent knocked this player in this fight.</summary>
    public bool WasKnocked { get; init; }

    /// <summary>This player's elimination in this fight is credited to the opponent.</summary>
    public bool WasEliminated { get; init; }

    /// <summary>Won, lost or open. When both knocked each other, the elimination decides; without one it is open.</summary>
    public FightOutcome Outcome { get; init; }

    /// <summary>
    /// Seconds from this player's first hit in the fight to knocking the opponent. <c>null</c> if the player did not knock the
    /// opponent in this fight. 0 when the first hit knocked.
    /// </summary>
    public float? TimeToKnock { get; init; }

    /// <summary>
    /// Seconds from this player's first hit in the fight to the opponent's elimination credited to this player, including the time
    /// to finish the knocked opponent. <c>null</c> if the elimination is not credited to this player in this fight.
    /// </summary>
    public float? TimeToKill { get; init; }
}

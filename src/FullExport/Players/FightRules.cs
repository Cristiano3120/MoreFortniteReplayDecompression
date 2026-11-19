namespace MoreFortniteReplayDecompression.Players;

/// <summary>
/// When a fight between two players (<see cref="FightEvent"/>) is over. The values were measured on the 11 server replays of the
/// skin cup (every player and every hit known):
/// <list type="bullet">
/// <item>
/// The pause between two hits alone does not tell it: within build fights players often do not hit each other for 30 to 60 s
/// and still knock each other afterwards, while a player who runs away can be hit again minutes later.
/// </item>
/// <item>
/// What tells it is the distance: during a long pause that ends in a knock the two stay close (more than 100 m apart for more
/// than 30 s in only 4 % of them), while after pauses of over 5 minutes they were more than 100 m apart for 272 s (median).
/// </item>
/// <item>
/// From zone 5 on (see <see cref="EndgameZone"/>) 94 % of the players have an enemy within 100 m, from zone 6 on all of them, so
/// the distance says nothing any more and only the time decides.
/// </item>
/// </list>
/// </summary>
public static class FightRules
{
    /// <summary>Before the endgame a fight ends when the two were more than this far apart (meters) ...</summary>
    public const float SeparationMeters = 100f;

    /// <summary>... for longer than this (seconds, without a break).</summary>
    public const float SeparationSeconds = 30f;

    /// <summary>Before the endgame a fight ends at the latest after this many seconds without a hit between the two.</summary>
    public const float MaxPauseSeconds = 180f;

    /// <summary>
    /// Number of finished storm shrinks from which on the endgame starts (zone 5 = after the 5th shrink ended). In the skin cup
    /// replays the safe zone then has a radius of about 190 m and 94 % of the players have an enemy within 100 m.
    /// </summary>
    public const int EndgameZone = 5;

    /// <summary>
    /// In the endgame a fight ends after this many seconds without a hit between the two. Players are always close there, so being
    /// hit "out of nowhere" after a minute is a new fight. Shorter would also split short breaks to reload, heal or edit.
    /// </summary>
    public const float EndgamePauseSeconds = 30f;
}

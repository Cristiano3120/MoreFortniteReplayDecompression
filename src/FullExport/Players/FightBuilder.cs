using MoreFortniteReplayDecompression.Match;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>Builds the fights (<see cref="FightEvent"/>) of all players from their hits, knocks and eliminations, see <see cref="FightRules"/>.</summary>
internal static class FightBuilder
{
    /// <summary>Distance between the two players is checked once per this many seconds during a pause.</summary>
    private const float DistanceStepSeconds = 1f;

    /// <summary>A knock or elimination belongs to a fight if it comes at most this long after the fight's last hit (the cue of the
    /// knocking hit can arrive after the knock).</summary>
    private const float KnockAfterLastHitSeconds = 2f;

    private sealed class Fight
    {
        public readonly List<HitEvent> Hits = [];
        public readonly List<EliminationEvent> Falls = [];
        public float Start => Hits[0].T;
        public float LastHit => Hits[^1].T;
    }

    /// <summary>Fills <see cref="PlayerStats.Fights"/> of every player. Needs the finished hits, eliminations and positions.</summary>
    public static void Assign(List<PlayerStats> players, IReadOnlyList<StormPhase> phases)
    {
        foreach (PlayerStats p in players)
        {
            p.Fights.Clear();
        }

        // Every pair of enemies that hit each other at least once, with all hits of both directions.
        Dictionary<(PlayerStats A, PlayerStats B), List<HitEvent>> pairs = [];
        foreach (PlayerStats attacker in players)
        {
            foreach (HitEvent hit in attacker.DamageDealtEvents)
            {
                if (hit.Victim is not { } victim || victim == attacker || hit.IsTeamDamage || hit.IsSelfDamage)
                {
                    continue;
                }

                var key = attacker.Channel.Value < victim.Channel.Value ? (attacker, victim) : (victim, attacker);
                if (!pairs.TryGetValue(key, out List<HitEvent>? hits))
                {
                    hits = [];
                    pairs[key] = hits;
                }

                hits.Add(hit);
            }
        }

        foreach (((PlayerStats a, PlayerStats b), List<HitEvent> hits) in pairs)
        {
            hits.Sort((x, y) => x.T.CompareTo(y.T));
            List<Fight> fights = Split(a, b, hits, phases);
            AssignFalls(a, b, fights);
            AssignFalls(b, a, fights);

            foreach (Fight fight in fights)
            {
                bool endgame = FinishedShrinks(phases, fight.Start) >= FightRules.EndgameZone;
                a.Fights.Add(View(a, b, fight, endgame));
                b.Fights.Add(View(b, a, fight, endgame));
            }
        }

        foreach (PlayerStats p in players)
        {
            p.Fights.Sort((x, y) => x.Start.CompareTo(y.Start));
        }
    }

    /// <summary>Splits the hits between two players into fights.</summary>
    private static List<Fight> Split(PlayerStats a, PlayerStats b, List<HitEvent> hits, IReadOnlyList<StormPhase> phases)
    {
        // Eliminations (not knocks) of either player end all their fights; a rebooted player starts new ones.
        List<float> deaths = [.. a.DeathEvents.Concat(b.DeathEvents).Where(e => !e.IsKnock).Select(e => e.T)];

        List<Fight> fights = [];
        Fight? current = null;
        foreach (HitEvent hit in hits)
        {
            if (current is null || Ended(a, b, current.LastHit, hit.T, deaths, phases))
            {
                current = new Fight();
                fights.Add(current);
            }

            current.Hits.Add(hit);
        }

        return fights;
    }

    /// <summary>Whether the fight whose last hit was at <paramref name="previous"/> was over before the hit at <paramref name="next"/>.</summary>
    private static bool Ended(PlayerStats a, PlayerStats b, float previous, float next, List<float> deaths, IReadOnlyList<StormPhase> phases)
    {
        float pause = next - previous;

        // The kill feed entry comes up to a moment before the cue of the hit that eliminated, so that hit still belongs to the fight.
        if (deaths.Any(t => t >= previous && t < next - KnockAfterLastHitSeconds))
        {
            return true;
        }

        if (FinishedShrinks(phases, next) >= FightRules.EndgameZone)
        {
            return pause > FightRules.EndgamePauseSeconds;
        }

        if (pause > FightRules.MaxPauseSeconds)
        {
            return true;
        }

        if (pause <= FightRules.SeparationSeconds)
        {
            return false;
        }

        // Apart for longer than SeparationSeconds without a break. An unknown position (not relevant for the recording) is no proof.
        float apartSince = float.NaN;
        for (float t = previous; t <= next; t += DistanceStepSeconds)
        {
            if (Distance(a, b, t) is { } meters && meters > FightRules.SeparationMeters)
            {
                if (float.IsNaN(apartSince))
                {
                    apartSince = t;
                }

                if (t - apartSince > FightRules.SeparationSeconds)
                {
                    return true;
                }
            }
            else
            {
                apartSince = float.NaN;
            }
        }

        return false;
    }

    /// <summary>Puts the knocks and eliminations of <paramref name="victim"/> credited to <paramref name="attacker"/> into the fight they belong to.</summary>
    private static void AssignFalls(PlayerStats attacker, PlayerStats victim, List<Fight> fights)
    {
        foreach (EliminationEvent fall in attacker.EliminationEvents)
        {
            if (fall.Eliminated != victim)
            {
                continue;
            }

            // The fight that was going on: started before the fall and its last hit not long before it. An elimination of a
            // knocked player belongs to the fight of the knock even if a teammate finished him much later.
            Fight? fight = fights.LastOrDefault(f => f.Start <= fall.T + KnockAfterLastHitSeconds && fall.T - f.LastHit <= KnockAfterLastHitSeconds);
            if (fight is null && !fall.IsKnock)
            {
                fight = fights.LastOrDefault(f => f.Falls.Any(k => k.IsKnock && k.Eliminated == victim && k.T <= fall.T));
            }

            fight?.Falls.Add(fall);
        }
    }

    private static FightEvent View(PlayerStats player, PlayerStats opponent, Fight fight, bool endgame)
    {
        List<HitEvent> dealt = [.. fight.Hits.Where(h => h.Attacker == player)];
        List<HitEvent> taken = [.. fight.Hits.Where(h => h.Attacker == opponent)];
        float? firstHit = dealt.Count > 0 ? dealt[0].T : null;

        EliminationEvent? knock = fight.Falls.FirstOrDefault(f => f.IsKnock && f.CreditedTo == player && f.Eliminated == opponent);
        EliminationEvent? kill = fight.Falls.FirstOrDefault(f => !f.IsKnock && f.CreditedTo == player && f.Eliminated == opponent);
        bool wasKnocked = fight.Falls.Any(f => f.IsKnock && f.CreditedTo == opponent && f.Eliminated == player);
        bool wasKilled = fight.Falls.Any(f => !f.IsKnock && f.CreditedTo == opponent && f.Eliminated == player);

        FightOutcome outcome = kill is not null ? FightOutcome.Won
            : wasKilled ? FightOutcome.Lost
            : knock is not null && !wasKnocked ? FightOutcome.Won
            : wasKnocked && knock is null ? FightOutcome.Lost
            : FightOutcome.Open;

        float end = Math.Max(fight.LastHit, fight.Falls.Count > 0 ? fight.Falls.Max(f => f.T) : float.NegativeInfinity);

        return new FightEvent
        {
            Opponent = opponent,
            Start = fight.Start,
            End = end,
            IsEndgame = endgame,
            FirstHitDealt = firstHit,
            HitsDealt = dealt.Count,
            HitsTaken = taken.Count,
            DamageDealt = dealt.Sum(h => h.Amount),
            DamageTaken = taken.Sum(h => h.Amount),
            KnockedOpponent = knock is not null,
            EliminatedOpponent = kill is not null,
            WasKnocked = wasKnocked,
            WasEliminated = wasKilled,
            Outcome = outcome,
            TimeToKnock = knock is not null && firstHit is { } k ? Math.Max(0, knock.T - k) : null,
            TimeToKill = kill is not null && firstHit is { } f2 ? Math.Max(0, kill.T - f2) : null,
        };
    }

    /// <summary>Number of storm shrinks that ended before <paramref name="t"/>.</summary>
    private static int FinishedShrinks(IReadOnlyList<StormPhase> phases, float t) => phases.Count(p => p.EndT <= t);

    /// <summary>Horizontal distance in meters, <c>null</c> if a position is unknown.</summary>
    private static float? Distance(PlayerStats a, PlayerStats b, float t)
    {
        if (a.PositionAt(t) is not FVector p || b.PositionAt(t) is not FVector q)
        {
            return null;
        }

        double dx = p.X - q.X, dy = p.Y - q.Y;
        return (float)(Math.Sqrt((dx * dx) + (dy * dy)) / 100);
    }
}

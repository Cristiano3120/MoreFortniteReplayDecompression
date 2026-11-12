using MoreFortniteReplayDecompression.Match;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

public sealed partial class PlayerStats
{
    /// <summary>Recomputes all sums and lists; only events from the match start on count.</summary>
    internal void Finish(FinishContext context)
    {
        MatchStartTime = context.MatchStart;
        float start = context.MatchStart ?? float.NegativeInfinity;

        FinishDamage(start);
        FinishEliminations(start, context.MatchEnd);
        FinishAssists(start);
        FinishHealth(start);
        FinishMovement(start, context);

        FinishBuilds(start);

        EquippedWeapons.Clear();
        EquippedWeapons.AddRange(RawWeaponChanges);
    }

    private void FinishBuilds(float start)
    {
        Builds.Clear();
        Materials used = new();

        foreach (BuildEvent build in RawBuilds.OrderBy(b => b.T))
        {
            if (build.T < start)
            {
                continue;
            }

            Builds.Add(build);
            used = used.Add(build.Material, MaterialsPerBuild);
        }

        MaterialsUsed = used;
    }

    private void FinishDamage(float start)
    {
        DamageDealtEvents.Clear();
        DamageTakenEvents.Clear();
        PreMatchDamageDealtEvents.Clear();
        PreMatchDamageTakenEvents.Clear();
        DamageDealt = 0; Hits = 0; CriticalHits = 0; ShieldHits = 0; FatalHits = 0; DownedHits = 0; PreMatchDamageDealt = 0; PreMatchHits = 0;
        DamageDealtToDowned = 0; DamageDealtToTeammates = 0; DamageDealtToSelf = 0;
        DownedHitsDuringProtection = 0; DownedHitsAfterProtection = 0;
        DamageDealtToDownedDuringProtection = 0; DamageDealtToDownedAfterProtection = 0;
        DamageTaken = 0; HitsTaken = 0; PreMatchDamageTaken = 0;
        DamageDealtToStructures = 0; StructureHits = 0;

        // Unknown as soon as one hit has no overkill value (the replay has no health data of that victim).
        float? overkill = 0;

        foreach (HitEvent hit in RawDealt)
        {
            if (hit.T < start)
            {
                PreMatchDamageDealt += hit.Amount;
                PreMatchHits++;
                PreMatchDamageDealtEvents.Add(hit);
                continue;
            }

            DamageDealtEvents.Add(hit);
            DamageDealt += hit.Amount;
            Hits++;
            overkill += hit.Overkill;

            if (hit.IsCritical)
            {
                CriticalHits++;
            }

            if (hit.IsShield)
            {
                ShieldHits++;
            }

            if (hit.IsFatal)
            {
                FatalHits++;
            }

            if (hit.VictimWasDowned)
            {
                DownedHits++;
                DamageDealtToDowned += hit.Amount;

                if (hit.IsDuringKnockProtection)
                {
                    DownedHitsDuringProtection++;
                    DamageDealtToDownedDuringProtection += hit.Amount;
                }
                else
                {
                    DownedHitsAfterProtection++;
                    DamageDealtToDownedAfterProtection += hit.Amount;
                }
            }

            if (hit.IsTeamDamage)
            {
                DamageDealtToTeammates += hit.Amount;
            }

            if (hit.IsSelfDamage)
            {
                DamageDealtToSelf += hit.Amount;
            }
        }

        OverkillDamage = overkill;

        foreach (HitEvent hit in RawTaken)
        {
            if (hit.T < start)
            {
                PreMatchDamageTaken += hit.Amount;
                PreMatchDamageTakenEvents.Add(hit);
                continue;
            }

            DamageTakenEvents.Add(hit);
            DamageTaken += hit.Amount;
            HitsTaken++;
        }

        foreach ((float t, float amount) in RawStructure)
        {
            if (t < start)
            {
                continue;
            }

            DamageDealtToStructures += amount;
            StructureHits++;
        }
    }

    private void FinishEliminations(float start, float matchEnd)
    {
        EliminationEvents.Clear();
        DeathEvents.Clear();
        Knocks = 0; Eliminations = 0; Deaths = 0;

        HashSet<PlayerStats> eliminated = [];
        foreach (EliminationEvent e in RawEliminations)
        {
            if (e.T < start || e.IsSelfElimination || e.IsTeamElimination)
            {
                continue;
            }

            EliminationEvents.Add(e);
            if (e.IsKnock)
            {
                Knocks++;
                continue;
            }

            Eliminations++;
            if (e.Eliminated is not null)
            {
                eliminated.Add(e.Eliminated);
            }
        }

        UniqueEliminations = eliminated.Count;

        DeathTime = null;
        foreach (EliminationEvent e in RawDeaths)
        {
            if (e.T < start)
            {
                continue;
            }

            DeathEvents.Add(e);
            if (!e.IsKnock)
            {
                Deaths++;
                DeathTime = e.T;
            }
        }

        // Rebooted after the last elimination and still alive at the end: the pawn moves again later.
        if (DeathTime is { } death && Positions.Any(p => p.T > death + MaxSampleGapSeconds))
        {
            DeathTime = null;
        }

        float alive = (DeathTime ?? matchEnd) - (float.IsNegativeInfinity(start) ? 0 : start);
        TimeAlive = Math.Max(0, alive);

        float deathTime = DeathTime ?? float.PositiveInfinity;
        EliminationsBeforeDeath = EliminationEvents.Count(e => !e.IsKnock && e.T <= deathTime);
    }

    /// <summary>
    /// Eliminations of enemies this player damaged in the <see cref="AssistWindowSeconds"/> before the enemy was knocked
    /// (or eliminated, if there was no knock), while the elimination is credited to someone else. Hits on the downed enemy do not count.
    /// </summary>
    private void FinishAssists(float start)
    {
        AssistEvents.Clear();

        IEnumerable<PlayerStats> victims = RawDealt
            .Where(h => h.T >= start && h.Amount > 0 && !h.VictimWasDowned && !h.IsTeamDamage && !h.IsSelfDamage && h.Victim is not null)
            .Select(h => h.Victim!)
            .Distinct();

        foreach (PlayerStats victim in victims)
        {
            foreach (EliminationEvent elimination in victim.RawDeaths)
            {
                if (elimination.IsKnock || elimination.T < start || elimination.CreditedTo is null || elimination.CreditedTo == this ||
                    elimination.IsSelfElimination || elimination.IsTeamElimination)
                {
                    continue;
                }

                float reference = victim.RawDeaths
                    .Where(k => k.IsKnock && k.T <= elimination.T && elimination.T - k.T <= MaxKnockAgeSeconds)
                    .Select(k => k.T)
                    .DefaultIfEmpty(elimination.T)
                    .Max();

                bool damaged = RawDealt.Any(h =>
                    h.Victim == victim &&
                    h.Amount > 0 &&
                    !h.VictimWasDowned &&
                    h.T <= reference + AssistLateHitSeconds &&
                    reference - h.T <= AssistWindowSeconds);

                if (damaged)
                {
                    AssistEvents.Add(elimination);
                }
            }
        }

        AssistEvents.Sort((a, b) => a.T.CompareTo(b.T));
    }

    private void FinishHealth(float start)
    {
        HealthLost = 0; HealthLossEvents = 0; PreMatchHealthLost = 0;
        HealthHealed = 0; ShieldHealed = 0;

        DownedIntervals.Clear();
        float? downedAt = null;
        foreach ((float t, bool downed) in RawDownedChanges)
        {
            if (downed)
            {
                downedAt ??= t;
            }
            else if (downedAt is { } since)
            {
                DownedIntervals.Add(new DownedInterval(since, t));
                downedAt = null;
            }
        }

        if (downedAt is { } open)
        {
            DownedIntervals.Add(new DownedInterval(open, null));
        }

        foreach ((float t, float amount) in RawHealthLoss)
        {
            if (t < start)
            {
                PreMatchHealthLost += amount;
                continue;
            }

            HealthLost += amount;
            HealthLossEvents++;
        }

        for (int i = 1; i < HealthTimeline.Count; i++)
        {
            HealthSample previous = HealthTimeline[i - 1];
            HealthSample current = HealthTimeline[i];
            if (current.T < start || IsDowned(previous.T) || IsDowned(current.T))
            {
                continue;
            }

            // From "dead or knocked" up is a revive, reboot or respawn, not healing.
            if (current.Health > previous.Health && previous.Health > DeadHealth)
            {
                HealthHealed += current.Health - previous.Health;
            }

            if (current.Shield > previous.Shield && previous.Health > DeadHealth)
            {
                ShieldHealed += current.Shield - previous.Shield;
            }
        }

        List<HealthSample> inMatch = HealthTimeline.Where(h => h.T >= start).ToList();
        MinHealth = inMatch.Count > 0 ? inMatch.Min(h => h.Health) : null;

        if (HealthTimeline.Count == 0)
        {
            FinalHealth = null;
            FinalShield = null;
        }
        else if (IsEliminated)
        {
            // After the elimination the health set is reset (100 hp) or negative; neither is the real final state.
            FinalHealth = 0;
            FinalShield = 0;
        }
        else
        {
            FinalHealth = Math.Max(0, HealthTimeline[^1].Health);
            FinalShield = Math.Max(0, HealthTimeline[^1].Shield);
        }
    }

    private void FinishMovement(float start, FinishContext context)
    {
        DistanceTraveled = (float)(RawDistance / 100);
        TimeInStorm = 0;
        StormDamageTaken = 0;
        SupplyDropDistances.Clear();

        float end = DeathTime ?? context.MatchEnd;

        for (int i = 0; i + 1 < Positions.Count; i++)
        {
            PositionSample sample = Positions[i];
            float dt = Positions[i + 1].T - sample.T;
            if (sample.T < start || sample.T > end || dt > MaxSampleGapSeconds)
            {
                continue;
            }

            if (Storm.IsInStorm(context.StormPhases, sample.T, sample.Location))
            {
                TimeInStorm += dt;
            }
        }

        foreach ((float t, float amount) in RawHealthLoss)
        {
            if (t < start || t > end)
            {
                continue;
            }

            bool hitAtTheSameTime = RawTaken.Any(h => Math.Abs(h.T - t) <= HitWindowSeconds);
            if (!hitAtTheSameTime && PositionAt(t) is { } location && Storm.IsInStorm(context.StormPhases, t, location))
            {
                StormDamageTaken += amount;
            }
        }

        foreach (SupplyDropInfo drop in context.SupplyDrops)
        {
            if (drop.SpawnT < start || drop.SpawnT > end)
            {
                continue;
            }

            FVector? target = drop.LandingLocation ?? drop.SpawnLocation;
            if (target is null || PositionAt(drop.SpawnT) is not { } location)
            {
                continue;
            }

            double dx = target.X - location.X;
            double dy = target.Y - location.Y;
            SupplyDropDistances.Add(new SupplyDropDistance(drop, (float)(Math.Sqrt((dx * dx) + (dy * dy)) / 100)));
        }
    }
}

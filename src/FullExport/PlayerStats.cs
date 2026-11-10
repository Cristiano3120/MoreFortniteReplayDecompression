using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Events;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// All values of one player. The key is the PlayerState channel.
/// Damage and hits only count from <see cref="MatchStartTime"/> (start of the Battle Bus);
/// damage on the starter island is kept separately in the <c>PreMatch*</c> properties.
/// Damage from hits (<see cref="DamageDealt"/>, <see cref="DamageTaken"/>) and damage derived from health changes
/// (<see cref="HealthLost"/>) are separate sources and can differ.
/// </summary>
public sealed class PlayerStats
{
    /// <summary>Health below or equal to this is "dead or knocked" (a knocked player shows 1 hp), increases from there are no healing.</summary>
    private const float DeadHealth = 1f;

    /// <summary>A health or shield loss within this many seconds of a hit belongs to the hit and is not storm damage.</summary>
    private const float HitWindowSeconds = 0.25f;

    /// <summary>Position samples further apart than this are a gap (pawn not relevant), not time spent at one place.</summary>
    private const float MaxSampleGapSeconds = 5f;

    public ChannelIndex Channel { get; internal set; }

    /// <summary>Replay time (seconds) from which damage counts: start of the Battle Bus. <c>null</c> = not detected, then everything counts.</summary>
    public float? MatchStartTime { get; internal set; }

    // Raw data over the whole replay; the public sums are computed in Finish() from MatchStartTime on.
    internal List<HitEvent> RawDealt { get; } = [];
    internal List<HitEvent> RawTaken { get; } = [];
    internal List<(float T, float Amount)> RawStructure { get; } = [];
    internal List<(float T, float Amount)> RawHealthLoss { get; } = [];
    internal List<(float T, bool Downed)> RawDownedChanges { get; } = [];
    internal List<WeaponChange> RawWeaponChanges { get; } = [];
    internal List<EliminationEvent> RawEliminations { get; } = [];
    internal List<EliminationEvent> RawDeaths { get; } = [];
    internal double RawDistance { get; set; }

    // --- Identity (from FortPlayerState; the name comes from the library's standard result)
    public int? Id { get; internal set; }
    public string? EpicId { get; internal set; }
    public string? BotId { get; internal set; }
    public bool IsBot { get; internal set; }
    public int? TeamIndex { get; internal set; }
    public int? Placement { get; internal set; }

    /// <summary>
    /// Kill score of the FortPlayerState (server value, the most reliable kill count). Matched Epic's end-of-match eliminations in 10 of 11
    /// skin cup matches; the one difference was a rebooted player that was eliminated a second time (counts here, not at Epic).
    /// </summary>
    public uint? Kills { get; internal set; }
    public uint? TeamKills { get; internal set; }

    /// <summary>The player who recorded the replay. Only this player has <see cref="EpicMatchStats"/>.</summary>
    public bool IsReplayOwner { get; internal set; }

    /// <summary>
    /// Epic's own end-of-match statistics of the replay owner (AthenaMatchStats event of the replay), <c>null</c> for everyone else.
    /// This is what Epic shows after the match and what tournament trackers use for eliminations.
    /// </summary>
    public Stats? EpicMatchStats { get; internal set; }

    /// <summary>Entry of the library's standard result (name, cosmetics, locations, ...), linked after reading.</summary>
    public PlayerData? Data { get; internal set; }

    public string? Name => Data?.PlayerName ?? Data?.PlayerNameCustomOverride ?? EpicId ?? BotId;

    // --- Eliminations (kill feed, from the match start, without self and team eliminations)
    /// <summary>Enemies this player knocked down.</summary>
    public int Knocks { get; private set; }

    /// <summary>
    /// Eliminations credited to this player (Fortnite's rule: the player who knocked the victim gets it, also if a teammate finished it).
    /// A rebooted player that is eliminated again counts twice.
    /// </summary>
    public int Eliminations { get; private set; }

    /// <summary>Different enemies credited to this player (a rebooted player counts once).</summary>
    public int UniqueEliminations { get; private set; }

    public List<EliminationEvent> EliminationEvents { get; } = [];

    /// <summary>How often this player was eliminated (more than once after a reboot).</summary>
    public int Deaths { get; private set; }

    public List<EliminationEvent> DeathEvents { get; } = [];

    /// <summary>Replay time of the final elimination of this player, <c>null</c> if the player survived until the end.</summary>
    public float? DeathTime { get; private set; }

    public bool IsEliminated => DeathTime is not null;

    /// <summary>Seconds from the match start until the final elimination (or the end of the match).</summary>
    public float TimeAlive { get; private set; }

    // --- Health / shield
    /// <summary>Complete health history including the time before the match start.</summary>
    public List<HealthSample> HealthTimeline { get; } = [];

    public float MaxHealth { get; private set; }
    public float MaxShield { get; private set; }

    /// <summary>Health at the end: 0 if the player was eliminated, otherwise the last known value. <c>null</c> if the replay has no health of this player.</summary>
    public float? FinalHealth { get; private set; }

    /// <summary>Shield at the end: 0 if the player was eliminated, otherwise the last known value.</summary>
    public float? FinalShield { get; private set; }

    /// <summary>Lowest health since the match start.</summary>
    public float? MinHealth { get; internal set; }

    /// <summary>Sum of health+shield decreases since the match start (storm, fall and self damage included).</summary>
    public float HealthLost { get; internal set; }

    public int HealthLossEvents { get; internal set; }

    /// <summary>Health+shield lost before the match start (starter island); not included in <see cref="HealthLost"/>.</summary>
    public float PreMatchHealthLost { get; internal set; }

    /// <summary>Health gained since the match start, without the jumps of a revive, a reboot or a respawn.</summary>
    public float HealthHealed { get; private set; }

    /// <summary>Shield gained since the match start.</summary>
    public float ShieldHealed { get; private set; }

    /// <summary>Times this player was knocked down.</summary>
    public List<DownedInterval> DownedIntervals { get; } = [];

    // --- Damage dealt (BatchedDamageCues, attacker = this player)
    /// <summary>
    /// includes dmg to knocked players
    /// </summary>
    public float DamageDealt { get; internal set; }
    public float DamageDealtWithoutKnocked => DamageDealt - DamageDealtToDowned;
    public int Hits { get; internal set; }
    public int CriticalHits { get; internal set; }
    public int ShieldHits { get; internal set; }
    public int FatalHits { get; internal set; }

    /// <summary>Hits on players that were already knocked. Fortnite sends them with 0 damage, so they count as hits but not as damage.</summary>
    public int DownedHits { get; internal set; }

    /// <summary>Damage to structures and other non-player objects (sum only).</summary>
    public float DamageDealtToStructures { get; internal set; }
    public int StructureHits { get; internal set; }
    public List<HitEvent> DamageDealtEvents { get; } = [];

    /// <summary>Damage dealt to players before the match start (starter island); not included in <see cref="DamageDealt"/>.</summary>
    public float PreMatchDamageDealt { get; internal set; }

    public int PreMatchHits { get; internal set; }

    /// <summary>The single hits dealt before the match start (starter island), so they can be checked.</summary>
    public List<HitEvent> PreMatchDamageDealtEvents { get; } = [];

    // Breakdown of DamageDealt (these amounts are already included in DamageDealt)
    public float DamageDealtToDowned { get; internal set; }
    public float DamageDealtToTeammates { get; internal set; }
    public float DamageDealtToSelf { get; internal set; }

    // --- Damage taken (BatchedDamageCues, victim = this player)
    public float DamageTaken { get; internal set; }
    public int HitsTaken { get; internal set; }
    public List<HitEvent> DamageTakenEvents { get; } = [];

    /// <summary>Damage taken before the match start; not included in <see cref="DamageTaken"/>.</summary>
    public float PreMatchDamageTaken { get; internal set; }

    /// <summary>The single hits taken before the match start.</summary>
    public List<HitEvent> PreMatchDamageTakenEvents { get; } = [];

    // --- Movement, storm, supply drops, weapons
    /// <summary>Pawn positions over the whole replay (at most one per position interval). Only while the pawn was relevant for the recording client.</summary>
    public List<PositionSample> Positions { get; } = [];

    /// <summary>Horizontal distance the pawn moved on the ground since the match start, in meters (skydiving, gliding and teleports excluded).</summary>
    public float DistanceTraveled { get; private set; }

    /// <summary>Seconds outside the safe zone since the match start (only while positions are known).</summary>
    public float TimeInStorm { get; private set; }

    /// <summary>Health+shield lost while in the storm without a hit at the same time. Needs health data (replay owner and team).</summary>
    public float StormDamageTaken { get; private set; }

    /// <summary>Distance to every supply drop of the match when it appeared (only while this player was alive and its position known).</summary>
    public List<SupplyDropDistance> SupplyDropDistances { get; } = [];

    /// <summary>Every weapon / item the player switched to, in replay order.</summary>
    public List<WeaponChange> EquippedWeapons { get; } = [];

    public List<PlayerStats> Teammates { get; } = [];

    /// <summary>Recomputes all sums and lists; only events from the match start on count.</summary>
    internal void Finish(FinishContext context)
    {
        MatchStartTime = context.MatchStart;
        float start = context.MatchStart ?? float.NegativeInfinity;

        FinishDamage(start);
        FinishEliminations(start, context.MatchEnd);
        FinishHealth(start);
        FinishMovement(start, context);

        EquippedWeapons.Clear();
        EquippedWeapons.AddRange(RawWeaponChanges);
    }

    private void FinishDamage(float start)
    {
        DamageDealtEvents.Clear();
        DamageTakenEvents.Clear();
        PreMatchDamageDealtEvents.Clear();
        PreMatchDamageTakenEvents.Clear();
        DamageDealt = 0; Hits = 0; CriticalHits = 0; ShieldHits = 0; FatalHits = 0; DownedHits = 0; PreMatchDamageDealt = 0; PreMatchHits = 0;
        DamageDealtToDowned = 0; DamageDealtToTeammates = 0; DamageDealtToSelf = 0;
        DamageTaken = 0; HitsTaken = 0; PreMatchDamageTaken = 0;
        DamageDealtToStructures = 0; StructureHits = 0;

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

    /// <summary>Position at replay time <paramref name="t"/> (nearest sample), <c>null</c> if no sample is close enough.</summary>
    public FVector? PositionAt(float t)
    {
        if (Positions.Count == 0)
        {
            return null;
        }

        int index = Positions.BinarySearch(new PositionSample(t, null!), Comparer<PositionSample>.Create((a, b) => a.T.CompareTo(b.T)));
        if (index < 0)
        {
            index = ~index;
        }

        PositionSample? best = null;
        foreach (int i in new[] { index - 1, index })
        {
            if (i >= 0 && i < Positions.Count && (best is null || Math.Abs(Positions[i].T - t) < Math.Abs(best.Value.T - t)))
            {
                best = Positions[i];
            }
        }

        return best is { } sample && Math.Abs(sample.T - t) <= MaxSampleGapSeconds
            ? sample.Location
            : null;
    }

    /// <summary><c>true</c> if the player was knocked down at replay time <paramref name="t"/>.</summary>
    public bool IsDowned(float t)
    {
        return DownedIntervals.Any(d => d.Contains(t));
    }

    /// <summary>
    /// Sets the properties <see cref="MaxHealth"/> and <see cref="MaxShield"/>
    /// </summary>
    internal void SetMaxHealthAndShield(float maxHealth, float maxShield)
    {
        MaxHealth = maxHealth;
        MaxShield = maxShield;
    }
}

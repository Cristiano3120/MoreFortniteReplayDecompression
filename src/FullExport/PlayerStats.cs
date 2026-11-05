using FortniteReplayReader.Models;
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
    public ChannelIndex Channel { get; internal set; }

    /// <summary>Replay time (seconds) from which damage counts: start of the Battle Bus. <c>null</c> = not detected, then everything counts.</summary>
    public float? MatchStartTime { get; internal set; }

    // Raw data over the whole replay; the public sums are computed in Finish() from MatchStartTime on.
    internal List<HitEvent> RawDealt { get; } = [];
    internal List<HitEvent> RawTaken { get; } = [];
    internal List<(float T, float Amount)> RawStructure { get; } = [];
    internal List<(float T, float Amount)> RawHealthLoss { get; } = [];

    // --- Identity (from FortPlayerState; the name comes from the library's standard result)
    public int? Id { get; internal set; }
    public string? EpicId { get; internal set; }
    public string? BotId { get; internal set; }
    public bool IsBot { get; internal set; }
    public int? TeamIndex { get; internal set; }
    public int? Placement { get; internal set; }
    public uint? Kills { get; internal set; }
    public uint? TeamKills { get; internal set; }

    /// <summary>Entry of the library's standard result (name, cosmetics, locations, ...), linked after reading.</summary>
    public PlayerData? Data { get; internal set; }

    public string? Name => Data?.PlayerName ?? Data?.PlayerNameCustomOverride ?? EpicId ?? BotId;

    // --- Health / shield
    /// <summary>Complete health history including the time before the match start.</summary>
    public List<HealthSample> HealthTimeline { get; } = [];

    public float MaxHealth { get; private set; }
    public float MaxShield { get; private set; }

    public float? FinalHealth => HealthTimeline.Count > 0 ? HealthTimeline[^1].Health : null;
    public float? FinalShield => HealthTimeline.Count > 0 ? HealthTimeline[^1].Shield : null;

    /// <summary>Lowest health since the match start.</summary>
    public float? MinHealth { get; internal set; }

    /// <summary>Sum of health+shield decreases since the match start (storm, fall and self damage included).</summary>
    public float HealthLost { get; internal set; }

    public int HealthLossEvents { get; internal set; }

    /// <summary>Health+shield lost before the match start (starter island); not included in <see cref="HealthLost"/>.</summary>
    public float PreMatchHealthLost { get; internal set; }

    // --- Damage dealt (BatchedDamageCues, attacker = this player)
    /// <summary>
    /// includes dmg to knocked players
    /// </summary>
    public float DamageDealt { get; internal set; }
    public int Hits { get; internal set; }
    public int CriticalHits { get; internal set; }
    public int ShieldHits { get; internal set; }
    public int FatalHits { get; internal set; }
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

    /// <summary>Recomputes all sums and lists; only events from <paramref name="matchStart"/> on count.</summary>
    internal void Finish(float? matchStart)
    {
        MatchStartTime = matchStart;
        var start = matchStart ?? float.NegativeInfinity;

        DamageDealtEvents.Clear();
        DamageTakenEvents.Clear();
        PreMatchDamageDealtEvents.Clear();
        PreMatchDamageTakenEvents.Clear();
        DamageDealt = 0; Hits = 0; CriticalHits = 0; ShieldHits = 0; FatalHits = 0; PreMatchDamageDealt = 0; PreMatchHits = 0;
        DamageDealtToDowned = 0; DamageDealtToTeammates = 0; DamageDealtToSelf = 0;
        DamageTaken = 0; HitsTaken = 0; PreMatchDamageTaken = 0;
        DamageDealtToStructures = 0; StructureHits = 0;
        HealthLost = 0; HealthLossEvents = 0; PreMatchHealthLost = 0;

        foreach (var hit in RawDealt)
        {
            if (hit.T < start) { PreMatchDamageDealt += hit.Amount; PreMatchHits++; PreMatchDamageDealtEvents.Add(hit); continue; }
            DamageDealtEvents.Add(hit);
            DamageDealt += hit.Amount;
            Hits++;
            if (hit.IsCritical) CriticalHits++;
            if (hit.IsShield) ShieldHits++;
            if (hit.IsFatal) FatalHits++;
            if (hit.VictimWasDowned)
            {
                DownedHits++;
                DamageDealtToDowned += hit.Amount;
            }
            if (hit.IsTeamDamage) DamageDealtToTeammates += hit.Amount;
            if (hit.IsSelfDamage) DamageDealtToSelf += hit.Amount;
        }

        foreach (var hit in RawTaken)
        {
            if (hit.T < start) { PreMatchDamageTaken += hit.Amount; PreMatchDamageTakenEvents.Add(hit); continue; }
            DamageTakenEvents.Add(hit);
            DamageTaken += hit.Amount;
            HitsTaken++;
        }

        foreach (var (t, amount) in RawStructure)
        {
            if (t < start) continue;
            DamageDealtToStructures += amount;
            StructureHits++;
        }

        foreach (var (t, amount) in RawHealthLoss)
        {
            if (t < start) { PreMatchHealthLost += amount; continue; }
            HealthLost += amount;
            HealthLossEvents++;
        }

        var inMatch = HealthTimeline.Where(h => h.T >= start).ToList();
        MinHealth = inMatch.Count > 0 ? inMatch.Min(h => h.Health) : null;
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

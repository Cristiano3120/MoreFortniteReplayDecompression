using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.Players;

namespace MoreFortniteReplayDecompression.Export;

/// <summary>
/// Controls what is read from a replay. Everything that is switched off is not parsed at all (the parser skips the data),
/// which makes reading faster and uses less memory.
/// </summary>
public sealed class ExportSettings
{
    /// <summary>Everything on, including the full typed timeline.</summary>
    public static ExportSettings Full => new();

    /// <summary>All player statistics, but no timeline (much leaner).</summary>
    public static ExportSettings StatsOnly => new()
    {
        CollectTimeline = false
    };

    /// <summary><c>true</c>: store every read export group as typed time series (<see cref="ReplayExport.Timeline"/>). Costs time and memory.</summary>
    public bool CollectTimeline { get; init; } = true;

    /// <summary>Seconds between two stored pawn positions per player (timeline and <see cref="PlayerStats.Positions"/>).</summary>
    public float PositionInterval { get; init; } = 1f;

    /// <summary>Health and shield of the players (<see cref="HealthSet"/>). Off: no health timeline, healing and storm damage.</summary>
    public bool TrackHealth { get; init; } = true;

    /// <summary>Damage cues (<see cref="BatchedDamageCues"/> and the gameplay cues of projectile hits). Off: no damage dealt / taken and no hits.</summary>
    public bool TrackHits { get; init; } = true;

    /// <summary>Pawn positions. Off: no time in storm, storm damage, distance traveled and supply drop distances.</summary>
    public bool TrackPositions { get; init; } = true;

    /// <summary>Weapon changes of the players (<see cref="PlayerStats.EquippedWeapons"/>).</summary>
    public bool TrackWeapons { get; init; } = true;

    /// <summary>Pieces the players built (<see cref="PlayerStats.Builds"/>, <see cref="PlayerStats.MaterialsUsed"/>). Off: build actors are not parsed.</summary>
    public bool TrackBuilds { get; init; } = true;

    /// <summary>
    /// Gameplay cues of the pawns in the timeline (<see cref="GameplayCueAdded"/>, <see cref="GameplayCueExecuted"/>,
    /// <see cref="GameplayCueAddedFromSpec"/>, <see cref="GameplayCueExecutedFromSpec"/>): effects such as healing, sliding or
    /// charging a weapon, with tag name, instigator and causer. Without <see cref="CollectTimeline"/> only the "executed" cues
    /// are parsed, and only for <see cref="TrackHits"/> (hits of projectile weapons such as the slingshot are reported as cues).
    /// </summary>
    public bool TrackGameplayCues { get; init; } = true;

    /// <summary>Further export group types of the library that should not be parsed, for example <c>typeof(SupplyDropLlama)</c>.</summary>
    public IReadOnlySet<Type> IgnoredExportGroups { get; init; } = new HashSet<Type>();

    /// <summary>All export group types the parser should skip, derived from the switches above.</summary>
    internal HashSet<Type> GetIgnoredTypes()
    {
        HashSet<Type> ignored = [.. IgnoredExportGroups];

        if (!TrackHealth)
        {
            ignored.Add(typeof(HealthSet));
        }

        if (!TrackBuilds)
        {
            ignored.Add(typeof(PlayerBuild));
        }

        // Cues only end up in the timeline, except the "executed" ones: hits of projectile weapons are reported that way.
        bool cuesInTimeline = TrackGameplayCues && CollectTimeline;
        if (!cuesInTimeline)
        {
            ignored.Add(typeof(GameplayCueAdded));
            ignored.Add(typeof(GameplayCueAddedFromSpec));
        }

        if (!cuesInTimeline && !TrackHits)
        {
            ignored.Add(typeof(GameplayCueExecuted));
            ignored.Add(typeof(GameplayCueExecutedFromSpec));
        }

        if (!TrackHits)
        {
            ignored.Add(typeof(BatchedDamageCues));
        }

        return ignored;
    }
}

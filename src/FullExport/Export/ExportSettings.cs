using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.Players;

namespace MoreFortniteReplayDecompression.Export;

/// <summary>
/// Controls what is read from a replay. Everything that is switched off is not parsed at all (the parser skips the data),
/// which makes reading faster and uses less memory.
/// <para>
/// Every switch is off by default, so <c>new ExportSettings { TrackHits = true }</c> reads the hits and nothing else that
/// costs time. Start from <see cref="StatsOnly"/> or <see cref="Full"/> to get everything, and change single switches
/// with <c>with</c>: <c>ExportSettings.StatsOnly with { TrackBuilds = false }</c>.
/// </para>
/// </summary>
public sealed record ExportSettings
{
    /// <summary>All player statistics and the full typed timeline. Only <see cref="TrackMovementDetails"/> stays off.</summary>
    public static ExportSettings Full => AllStats(collectTimeline: true);

    /// <summary>
    /// Only the metadata of the replay (<see cref="ReplayExport.Metadata"/>: time, length, tournament, playlist, ...), as fast as possible.
    /// No players, hits or timeline; see <see cref="MetadataOnly"/>.
    /// </summary>
    public static ExportSettings Metadata => new()
    {
        MetadataOnly = true,
    };

    /// <summary>All player statistics, but no timeline (much leaner). Only <see cref="TrackMovementDetails"/> stays off.</summary>
    public static ExportSettings StatsOnly => AllStats(collectTimeline: false);

    /// <summary>Every <c>Track</c> switch on except <see cref="TrackMovementDetails"/>.</summary>
    internal static ExportSettings AllStats(bool collectTimeline, float positionInterval = 1f) => new()
    {
        CollectTimeline = collectTimeline,
        PositionInterval = positionInterval,
        TrackHealth = true,
        TrackHits = true,
        TrackShots = true,
        TrackPositions = true,
        TrackWeapons = true,
        TrackWeaponItems = true,
        TrackBuilds = true,
        TrackGameplayCues = true,
    };

    /// <summary>
    /// <c>true</c>: only read the metadata of the replay (<see cref="ReplayExport.Metadata"/>) and nothing else. Only the start of the
    /// replay is read and the reading stops as soon as the values are known (usually after the first one to four data chunks),
    /// so this takes a few milliseconds instead of seconds. <see cref="ReplayExport.Players"/>, <see cref="ReplayExport.Bots"/>,
    /// <see cref="ReplayExport.Timeline"/> and the other match data stay empty, and all other settings are ignored.
    /// Shortcut: <see cref="ReplayExporter.ReadMetadata(string)"/>.
    /// </summary>
    public bool MetadataOnly { get; init; }

    /// <summary><c>true</c>: store every read export group as typed time series (<see cref="ReplayExport.Timeline"/>). Costs time and memory.</summary>
    public bool CollectTimeline { get; init; }

    /// <summary>
    /// Seconds between two stored pawn positions per player (timeline and <see cref="PlayerStats.Positions"/>).
    /// 0 = store every movement update. The distance traveled always uses every update, whatever the interval.
    /// Time in storm and supply drop distances are computed from the stored positions, so a smaller interval makes them finer.
    /// </summary>
    public float PositionInterval { get; init; } = 1f;

    /// <summary>Health and shield of the players (<see cref="HealthSet"/>). Off: no health timeline, healing and storm damage.</summary>
    public bool TrackHealth { get; init; }

    /// <summary>Damage cues (<see cref="BatchedDamageCues"/> and the gameplay cues of projectile hits). Off: no damage dealt / taken and no hits.</summary>
    public bool TrackHits { get; init; }

    /// <summary>
    /// Shots fired per weapon and accuracy (<see cref="PlayerStats.ShotsFired"/>). Off: the ammo and fire time of the weapons are not read.
    /// </summary>
    public bool TrackShots { get; init; }

    /// <summary>
    /// Pawn positions. Off: no time in storm, storm damage, distance traveled and supply drop distances.
    /// With <see cref="TrackBuilds"/> also off (and no timeline) the movement of the pawns is not decoded at all, which is
    /// about a quarter of the read time.
    /// </summary>
    public bool TrackPositions { get; init; }

    /// <summary>
    /// Velocity, rotation and states (crouched, sprinting, aiming, skydiving, ...) for every stored position
    /// (<see cref="PlayerStats.Movement"/>). Needs <see cref="TrackPositions"/> and follows <see cref="PositionInterval"/>.
    /// Also off in <see cref="Full"/> and <see cref="StatsOnly"/> because it needs a lot of memory with a small interval.
    /// Off: <see cref="PlayerStats.Movement"/> stays empty.
    /// </summary>
    public bool TrackMovementDetails { get; init; }

    /// <summary>
    /// Item id and rarity of the weapons (<see cref="HitEvent.WeaponItem"/>, <see cref="HitEvent.WeaponRarity"/>,
    /// <see cref="WeaponShots.Items"/>), for example <c>WID_Shotgun_Break_Flintlock_Athena_SR</c> instead of only the weapon class.
    /// Off: the <c>WeaponData</c> of the weapons is not read and these values stay empty.
    /// </summary>
    public bool TrackWeaponItems { get; init; }

    /// <summary>Weapon changes of the players (<see cref="PlayerStats.EquippedWeapons"/>).</summary>
    public bool TrackWeapons { get; init; }

    /// <summary>Pieces the players built (<see cref="PlayerStats.Builds"/>, <see cref="PlayerStats.MaterialsUsed"/>). Off: build actors are not parsed.</summary>
    public bool TrackBuilds { get; init; }

    /// <summary>
    /// Gameplay cues of the pawns in the timeline (<see cref="GameplayCueAdded"/>, <see cref="GameplayCueExecuted"/>,
    /// <see cref="GameplayCueAddedFromSpec"/>, <see cref="GameplayCueExecutedFromSpec"/>): effects such as healing, sliding or
    /// charging a weapon, with tag name, instigator and causer. Without <see cref="CollectTimeline"/> only the "executed" cues
    /// are parsed, and only for <see cref="TrackHits"/> (hits of projectile weapons such as the slingshot are reported as cues).
    /// </summary>
    public bool TrackGameplayCues { get; init; }

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

        // Neither the stats nor the library's result use these; they only end up in the timeline. Team info alone was about
        // a quarter of the property parsing time of a server replay.
        if (!CollectTimeline)
        {
            ignored.Add(typeof(FortTeamPrivateInfo));
            ignored.Add(typeof(FortPickup));
            ignored.Add(typeof(FortInventory));
        }

        return ignored;
    }

    /// <summary>
    /// Pawn properties the stats and the library's result need. Without the timeline all other pawn properties are skipped by
    /// their bit length instead of being decoded: about 80 % of all pawn values in a replay are camera and input data
    /// (view rotation, recoil, acceleration, input) that nothing reads.
    /// </summary>
    private static readonly string[] PawnPropertiesForStats =
    [
        // Player, knocks, weapon, skydiving (FullExportReader.TrackPlayers) and positions (dropped again in GetOnlyProperties if unused)
        nameof(PlayerPawn.PlayerState), nameof(PlayerPawn.bIsDBNO), nameof(PlayerPawn.CurrentWeapon),
        nameof(PlayerPawn.bIsSkydiving), nameof(PlayerPawn.bIsParachuteOpen), nameof(PlayerPawn.ReplicatedMovement),

        // Cosmetics of the library's PlayerData (FortniteReplayBuilder.UpdatePlayerPawn)
        nameof(PlayerPawn.Character), nameof(PlayerPawn.BannerColorId), nameof(PlayerPawn.BannerIconId), nameof(PlayerPawn.bIsDefaultCharacter),
        nameof(PlayerPawn.Backpack), nameof(PlayerPawn.PetSkin), nameof(PlayerPawn.Glider), nameof(PlayerPawn.LoadingScreen),
        nameof(PlayerPawn.MusicPack), nameof(PlayerPawn.Pickaxe), nameof(PlayerPawn.SkyDiveContrail), nameof(PlayerPawn.Dances),
        nameof(PlayerPawn.ItemWraps),
    ];

    /// <summary>Pawn properties of <see cref="PlayerStats.Movement"/> (<see cref="TrackMovementDetails"/>).</summary>
    private static readonly string[] PawnPropertiesForMovementDetails =
    [
        nameof(PlayerPawn.bIsCrouched), nameof(PlayerPawn.bIsJumping), nameof(PlayerPawn.bIsTacticalSprinting), nameof(PlayerPawn.bIsTargeting),
        nameof(PlayerPawn.bIsSlopeSliding), nameof(PlayerPawn.bIsSkydivingFromBus), nameof(PlayerPawn.bIsSkydivingFromLaunchPad),
        nameof(PlayerPawn.bInGliderRedeploy), nameof(PlayerPawn.bIsWaterSprintBoost), nameof(PlayerPawn.bIsWaterJump),
        nameof(PlayerPawn.bIsPlayingEmote), nameof(PlayerPawn.bWeaponHolstered), nameof(PlayerPawn.bIsInAnyStorm),
    ];

    /// <summary>
    /// Per export group type the only properties the parser should decode, <c>null</c> to decode everything.
    /// The timeline shows every property, so this only applies without it.
    /// </summary>
    internal Dictionary<Type, IReadOnlySet<string>>? GetOnlyProperties()
    {
        if (CollectTimeline)
        {
            return null;
        }

        HashSet<string> pawn = [.. PawnPropertiesForStats];

        // The movement is in almost every pawn update. Only the positions and the builds (who stood next to a new piece) use it.
        if (!TrackPositions && !TrackBuilds)
        {
            pawn.Remove(nameof(PlayerPawn.ReplicatedMovement));
        }
        if (TrackMovementDetails)
        {
            pawn.UnionWith(PawnPropertiesForMovementDetails);
        }

        return new Dictionary<Type, IReadOnlySet<string>> { [typeof(PlayerPawn)] = pawn };
    }
}

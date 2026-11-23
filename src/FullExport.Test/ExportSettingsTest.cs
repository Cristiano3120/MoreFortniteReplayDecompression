using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.Export;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

public class ExportSettingsTest
{
    [Fact]
    public void EverySwitchIsOffByDefault()
    {
        ExportSettings settings = new();

        Assert.False(settings.CollectTimeline);
        Assert.False(settings.TrackHealth);
        Assert.False(settings.TrackHits);
        Assert.False(settings.TrackShots);
        Assert.False(settings.TrackPositions);
        Assert.False(settings.TrackWeapons);
        Assert.False(settings.TrackBuilds);
        Assert.False(settings.TrackGameplayCues);
        Assert.False(settings.TrackMovementDetails);
        Assert.False(settings.MetadataOnly);
        Assert.Contains(typeof(HealthSet), settings.GetIgnoredTypes());
        Assert.Contains(typeof(BatchedDamageCues), settings.GetIgnoredTypes());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullAndStatsOnlyTrackEverythingButMovementDetails(bool full)
    {
        ExportSettings settings = full ? ExportSettings.Full : ExportSettings.StatsOnly;

        Assert.Equal(full, settings.CollectTimeline);
        Assert.True(settings.TrackHealth);
        Assert.True(settings.TrackHits);
        Assert.True(settings.TrackShots);
        Assert.True(settings.TrackPositions);
        Assert.True(settings.TrackWeapons);
        Assert.True(settings.TrackBuilds);
        Assert.True(settings.TrackGameplayCues);
        Assert.False(settings.TrackMovementDetails);
        Assert.False(settings.MetadataOnly);
    }

    [Fact]
    public void FullParsesEveryGroup()
    {
        Assert.Empty(ExportSettings.Full.GetIgnoredTypes());
    }

    [Fact]
    public void WithChangesOnlyTheGivenSwitch()
    {
        ExportSettings settings = ExportSettings.StatsOnly with { TrackBuilds = false };

        Assert.False(settings.TrackBuilds);
        Assert.True(settings.TrackHits);
        Assert.True(ExportSettings.StatsOnly.TrackBuilds);
    }

    [Fact]
    public void MetadataReadsOnlyMetadata()
    {
        Assert.True(ExportSettings.Metadata.MetadataOnly);
        Assert.False(ExportSettings.Metadata.CollectTimeline);
    }

    [Fact]
    public void SwitchedOffGroupsAreNotParsed()
    {
        HashSet<Type> ignored = (ExportSettings.Full with { TrackHealth = false, TrackBuilds = false }).GetIgnoredTypes();

        Assert.Contains(typeof(HealthSet), ignored);
        Assert.Contains(typeof(PlayerBuild), ignored);
    }

    [Fact]
    public void ExecutedCuesAreStillParsedForHitsWithoutTimeline()
    {
        // Projectile hits (slingshot, ...) come as executed gameplay cues, so they are needed for the hits
        HashSet<Type> ignored = ExportSettings.StatsOnly.GetIgnoredTypes();

        Assert.Contains(typeof(GameplayCueAdded), ignored);
        Assert.Contains(typeof(GameplayCueAddedFromSpec), ignored);
        Assert.DoesNotContain(typeof(GameplayCueExecuted), ignored);
        Assert.DoesNotContain(typeof(GameplayCueExecutedFromSpec), ignored);
        Assert.DoesNotContain(typeof(BatchedDamageCues), ignored);
    }

    [Fact]
    public void NoHitsAndNoTimelineSkipsAllDamageCues()
    {
        HashSet<Type> ignored = (ExportSettings.StatsOnly with { TrackHits = false }).GetIgnoredTypes();

        Assert.Contains(typeof(GameplayCueExecuted), ignored);
        Assert.Contains(typeof(GameplayCueExecutedFromSpec), ignored);
        Assert.Contains(typeof(BatchedDamageCues), ignored);
    }

    [Fact]
    public void OwnIgnoredGroupsArePassedOn()
    {
        HashSet<Type> ignored = (ExportSettings.Full with { IgnoredExportGroups = new HashSet<Type> { typeof(SupplyDropLlama) } }).GetIgnoredTypes();

        Assert.Equal([typeof(SupplyDropLlama)], ignored);
    }

    [Fact]
    public void WithoutTimelineUnusedGroupsAreNotParsed()
    {
        HashSet<Type> ignored = ExportSettings.StatsOnly.GetIgnoredTypes();

        Assert.Contains(typeof(FortTeamPrivateInfo), ignored);
        Assert.Contains(typeof(FortPickup), ignored);
        Assert.Contains(typeof(FortInventory), ignored);
        Assert.DoesNotContain(typeof(FortTeamPrivateInfo), ExportSettings.Full.GetIgnoredTypes());
    }

    [Fact]
    public void WithoutTimelineOnlyNeededPawnPropertiesAreRead()
    {
        IReadOnlySet<string> pawn = ExportSettings.StatsOnly.GetOnlyProperties()![typeof(PlayerPawn)];

        Assert.Contains(nameof(PlayerPawn.ReplicatedMovement), pawn);
        Assert.Contains(nameof(PlayerPawn.CurrentWeapon), pawn);
        Assert.Contains(nameof(PlayerPawn.Character), pawn);
        Assert.DoesNotContain(nameof(PlayerPawn.RemoteViewData32), pawn);
        Assert.DoesNotContain(nameof(PlayerPawn.bIsCrouched), pawn);
        Assert.Null(ExportSettings.Full.GetOnlyProperties());
    }

    [Fact]
    public void MovementDetailsAddTheirPawnProperties()
    {
        IReadOnlySet<string> pawn = new ExportSettings { TrackMovementDetails = true }.GetOnlyProperties()![typeof(PlayerPawn)];

        Assert.Contains(nameof(PlayerPawn.bIsCrouched), pawn);
        Assert.Contains(nameof(PlayerPawn.bIsInAnyStorm), pawn);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void MovementIsOnlyReadForPositionsOrBuilds(bool positions, bool builds, bool read)
    {
        IReadOnlySet<string> pawn = new ExportSettings { TrackPositions = positions, TrackBuilds = builds }.GetOnlyProperties()![typeof(PlayerPawn)];

        Assert.Equal(read, pawn.Contains(nameof(PlayerPawn.ReplicatedMovement)));
    }

    [Fact]
    public void EveryFilteredPawnPropertyExists()
    {
        IReadOnlySet<string> pawn = (ExportSettings.StatsOnly with { TrackMovementDetails = true }).GetOnlyProperties()![typeof(PlayerPawn)];

        Assert.All(pawn, name => Assert.NotNull(typeof(PlayerPawn).GetProperty(name)));
    }
}

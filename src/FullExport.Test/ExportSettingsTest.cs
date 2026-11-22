using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.Export;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

public class ExportSettingsTest
{
    [Fact]
    public void DefaultsReadEverythingButMovementDetails()
    {
        ExportSettings settings = new();

        Assert.True(settings.CollectTimeline);
        Assert.True(settings.TrackHealth);
        Assert.True(settings.TrackHits);
        Assert.True(settings.TrackShots);
        Assert.True(settings.TrackPositions);
        Assert.True(settings.TrackWeapons);
        Assert.True(settings.TrackBuilds);
        Assert.True(settings.TrackGameplayCues);
        Assert.False(settings.TrackMovementDetails);
        Assert.False(settings.MetadataOnly);
        Assert.Empty(settings.GetIgnoredTypes());
    }

    [Fact]
    public void StatsOnlyHasNoTimeline()
    {
        Assert.False(ExportSettings.StatsOnly.CollectTimeline);
    }

    [Fact]
    public void SwitchedOffGroupsAreNotParsed()
    {
        HashSet<Type> ignored = new ExportSettings { TrackHealth = false, TrackBuilds = false }.GetIgnoredTypes();

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
        HashSet<Type> ignored = new ExportSettings { CollectTimeline = false, TrackHits = false }.GetIgnoredTypes();

        Assert.Contains(typeof(GameplayCueExecuted), ignored);
        Assert.Contains(typeof(GameplayCueExecutedFromSpec), ignored);
        Assert.Contains(typeof(BatchedDamageCues), ignored);
    }

    [Fact]
    public void OwnIgnoredGroupsArePassedOn()
    {
        HashSet<Type> ignored = new ExportSettings { IgnoredExportGroups = new HashSet<Type> { typeof(SupplyDropLlama) } }.GetIgnoredTypes();

        Assert.Equal([typeof(SupplyDropLlama)], ignored);
    }
}

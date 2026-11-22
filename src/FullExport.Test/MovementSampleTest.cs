using FortniteReplayReader.Models.NetFieldExports;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.Reading;
using Unreal.Core.Models;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

public class MovementSampleTest
{
    private static MovementSample Sample(double vx, double vy, double vz, MovementFlags flags = MovementFlags.None)
        => new(0, new FVector(0, 0, 0), new FVector(vx, vy, vz), new FRotator(0, 0, 0), flags);

    [Fact]
    public void SpeedIsHorizontalInMetersPerSecond()
    {
        // 300 and 400 cm/s horizontal = 5 m/s, falling speed does not count
        Assert.Equal(5f, Sample(300, 400, -2000).Speed, 0.001f);
        Assert.Equal(0f, Sample(0, 0, 500).Speed);
    }

    [Fact]
    public void HasNeedsAllGivenFlags()
    {
        MovementSample sample = Sample(0, 0, 0, MovementFlags.Crouched | MovementFlags.Targeting);

        Assert.True(sample.Has(MovementFlags.Crouched));
        Assert.True(sample.Has(MovementFlags.Crouched | MovementFlags.Targeting));
        Assert.False(sample.Has(MovementFlags.Crouched | MovementFlags.Sprinting));
        Assert.True(sample.Has(MovementFlags.None));
    }

    [Fact]
    public void SentStatesSetAndClearFlags()
    {
        MovementFlags flags = FullExportReader.UpdateMovementFlags(MovementFlags.None, new PlayerPawn { bIsCrouched = true, bIsTacticalSprinting = true });
        Assert.Equal(MovementFlags.Crouched | MovementFlags.Sprinting, flags);

        flags = FullExportReader.UpdateMovementFlags(flags, new PlayerPawn { bIsCrouched = false });
        Assert.Equal(MovementFlags.Sprinting, flags);
    }

    [Fact]
    public void StatesThatAreNotSentAreCarriedOver()
    {
        // The replay only sends a state when it changes, an update without it keeps the old one
        MovementFlags flags = FullExportReader.UpdateMovementFlags(MovementFlags.Crouched | MovementFlags.Downed, new PlayerPawn { bIsTargeting = true });

        Assert.Equal(MovementFlags.Crouched | MovementFlags.Downed | MovementFlags.Targeting, flags);
    }

    [Fact]
    public void EveryFlagHasItsOwnPawnState()
    {
        PlayerPawn all = new()
        {
            bIsCrouched = true, bIsJumping = true, bIsTacticalSprinting = true, bIsTargeting = true, bIsSlopeSliding = true,
            bIsSkydiving = true, bIsParachuteOpen = true, bIsSkydivingFromBus = true, bIsSkydivingFromLaunchPad = true,
            bInGliderRedeploy = true, bIsWaterSprintBoost = true, bIsWaterJump = true, bIsDBNO = true, bIsPlayingEmote = true,
            bWeaponHolstered = true, bIsInAnyStorm = true,
        };

        MovementFlags every = Enum.GetValues<MovementFlags>().Aggregate(MovementFlags.None, (a, b) => a | b);
        Assert.Equal(every, FullExportReader.UpdateMovementFlags(MovementFlags.None, all));
    }
}

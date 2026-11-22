using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>
/// Detailed movement of a player's pawn at one moment (<see cref="PlayerStats.Movement"/>).
/// Only collected with <see cref="Export.ExportSettings.TrackMovementDetails"/>.
/// </summary>
/// <param name="T">Replay time in seconds since the start of the recording (not the in-game clock).</param>
/// <param name="Location">Position in world space (cm), the same as the <see cref="PositionSample"/> at this time.</param>
/// <param name="Velocity">Velocity in world space (cm/s).</param>
/// <param name="Rotation">Rotation of the pawn. Usually only the yaw (the direction the player faces) is set.</param>
/// <param name="Flags">
/// What the pawn was doing at this moment (crouched, sprinting, skydiving, ...). The replay only sends a state when it changes,
/// so the last known state is carried over to every sample.
/// </param>
public readonly record struct MovementSample(float T, FVector Location, FVector Velocity, FRotator Rotation, MovementFlags Flags)
{
    /// <summary>Horizontal speed in m/s (without falling or jumping).</summary>
    public float Speed => (float)(Math.Sqrt((Velocity.X * Velocity.X) + (Velocity.Y * Velocity.Y)) / 100);

    /// <summary>Whether all given flags are set, for example <c>sample.Has(MovementFlags.Crouched)</c>.</summary>
    public bool Has(MovementFlags flags) => (Flags & flags) == flags;
}

/// <summary>
/// States of a pawn in a <see cref="MovementSample"/>. Several can be set at the same time (for example <see cref="Crouched"/> and <see cref="Targeting"/>).
/// </summary>
[Flags]
public enum MovementFlags : uint
{
    None = 0,

    /// <summary>Crouching (<c>bIsCrouched</c>).</summary>
    Crouched = 1 << 0,

    /// <summary>In a jump (<c>bIsJumping</c>). Not sent for the recording player in a client replay.</summary>
    Jumping = 1 << 1,

    /// <summary>Sprinting (<c>bIsTacticalSprinting</c>; the older <c>bIsSprinting</c> is not sent anymore).</summary>
    Sprinting = 1 << 2,

    /// <summary>Aiming down sights (<c>bIsTargeting</c>).</summary>
    Targeting = 1 << 3,

    /// <summary>Sliding down a slope (<c>bIsSlopeSliding</c>).</summary>
    SlopeSliding = 1 << 4,

    /// <summary>Skydiving, glider closed (<c>bIsSkydiving</c>).</summary>
    Skydiving = 1 << 5,

    /// <summary>Glider open (<c>bIsParachuteOpen</c>).</summary>
    Gliding = 1 << 6,

    /// <summary>Skydiving after jumping off the Battle Bus (<c>bIsSkydivingFromBus</c>).</summary>
    SkydivingFromBus = 1 << 7,

    /// <summary>Skydiving after a launch pad (<c>bIsSkydivingFromLaunchPad</c>).</summary>
    SkydivingFromLaunchPad = 1 << 8,

    /// <summary>Glider redeploy (<c>bInGliderRedeploy</c>).</summary>
    GliderRedeploy = 1 << 9,

    /// <summary>Speed boost while sprinting in water (<c>bIsWaterSprintBoost</c>).</summary>
    WaterSprintBoost = 1 << 10,

    /// <summary>Jumping out of water (<c>bIsWaterJump</c>).</summary>
    WaterJump = 1 << 11,

    /// <summary>Knocked (<c>bIsDBNO</c>).</summary>
    Downed = 1 << 12,

    /// <summary>Playing an emote (<c>bIsPlayingEmote</c>).</summary>
    PlayingEmote = 1 << 13,

    /// <summary>Weapon holstered (<c>bWeaponHolstered</c>).</summary>
    WeaponHolstered = 1 << 14,

    /// <summary>In the storm (<c>bIsInAnyStorm</c>). Mostly only sent in server replays.</summary>
    InStorm = 1 << 15,
}

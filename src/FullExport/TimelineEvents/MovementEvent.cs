using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.TimelineEvents;

/// <summary>
/// Movement part of one <see cref="PlayerPawn"/> update (position, rotation, velocity, view direction, ...).
/// <para>
/// Pawn updates are split: the movement fields go into this small entry and only the other fields (weapon, crouched,
/// skydiving, ...) are stored as an <see cref="ExportEvent"/> with a <see cref="PlayerPawn"/>, which then has all movement
/// fields empty. A full <see cref="PlayerPawn"/> copy has room for about 160 fields although a movement update only sets
/// about 4 of them, so this saves several hundred MB per server replay.
/// </para>
/// Like on the pawn, only the fields that were sent in this update are set; the rest is <c>null</c>.
/// The property names are the ones of <see cref="PlayerPawn"/>.
/// </summary>
public sealed record MovementEvent : TimelineEvent
{
    /// <summary>Class name of the pawn: <c>PlayerPawn</c> for players, <c>PhoebePlayerPawn</c> for bots, <c>MangPlayerPawn</c> for NPCs.</summary>
    public required string PawnType { get; init; }

    /// <summary>Location, rotation and velocity. See <see cref="PlayerPawn.ReplicatedMovement"/>.</summary>
    public FRepMovement? ReplicatedMovement { get; init; }

    /// <summary>Packed view direction (pitch and yaw) of the pawn.</summary>
    public uint? RemoteViewData32 { get; init; }

    public byte? RemoteViewPitch { get; init; }
    public ushort? AccelerationPack { get; init; }
    public byte? AccelerationZPack { get; init; }
    public float? ReplayLastTransformUpdateTimeStamp { get; init; }
    public ushort? PackedReplicatedSlopeAngles { get; init; }
    public float? GravityScale { get; init; }
    public FVector? Location { get; init; }
    public FRotator? Rotation { get; init; }
    public FVector? LinearVelocity { get; init; }
    public float? Position { get; init; }
    public FVector? WorldLookDir { get; init; }
    public float? ForwardAlpha { get; init; }
    public float? RightAlpha { get; init; }
    public float? TurnDelta { get; init; }
    public float? SteerAlpha { get; init; }
}

using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>A supply drop of the match.</summary>
public sealed record SupplyDropInfo
{
    /// <summary>Replay time at which the drop appeared.</summary>
    public required float SpawnT { get; init; }

    /// <summary>Position at which the drop appeared (in the air).</summary>
    public FVector? SpawnLocation { get; init; }

    /// <summary>Position at which the drop lands. <c>null</c> if the replay did not contain it.</summary>
    public FVector? LandingLocation { get; init; }

    public bool Looted { get; init; }
}

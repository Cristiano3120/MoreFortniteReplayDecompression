using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// One shrink of the storm. Between <see cref="StartT"/> and <see cref="EndT"/> the safe zone moves from
/// (<see cref="FromCenter"/>, <see cref="FromRadius"/>) to (<see cref="ToCenter"/>, <see cref="ToRadius"/>).
/// Times are replay times (seconds since the start of the recording), distances are Unreal units (cm).
/// </summary>
public sealed record StormPhase
{
    public required int Index { get; init; }
    public required float StartT { get; init; }
    public required float EndT { get; init; }

    /// <summary>Center before the shrink. For the first phase it is unknown and <see cref="ToCenter"/> is used instead.</summary>
    public required FVector FromCenter { get; init; }
    public required float FromRadius { get; init; }
    public required FVector ToCenter { get; init; }
    public required float ToRadius { get; init; }
}

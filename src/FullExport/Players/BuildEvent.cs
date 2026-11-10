using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>A piece the player built (not an edit). Every piece costs <see cref="PlayerStats.MaterialsPerBuild"/> materials.</summary>
/// <param name="T">Replay time in seconds since the start of the recording.</param>
/// <param name="Material">Wood, stone or metal.</param>
/// <param name="Piece">Piece type from the class name, for example <c>Solid</c> (wall), <c>Floor</c>, <c>StairW</c> or <c>RoofC</c>.</param>
/// <param name="Location">Location of the piece.</param>
public readonly record struct BuildEvent(float T, BuildMaterial Material, string Piece, FVector? Location);

using System.Text.RegularExpressions;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Reading;

public sealed partial class FullExportReader
{
    /// <summary>Class name of a player built piece: material letter (W, S, M) and piece type, for example <c>PBWA_S1_Solid_C</c>.</summary>
    private static readonly Regex BuildArchetype = new(@"PBWA_(?<material>[WSM])\d_(?<piece>\w+?)_C$", RegexOptions.Compiled);

    /// <summary>Only pawns at most this far away from a new piece (cm) can have built it.</summary>
    private const double MaxBuildDistance = 2000;

    /// <summary>A tool that was put away at most this many seconds before the piece appeared still counts as held.</summary>
    private const float ToolSwitchToleranceSeconds = 0.3f;

    /// <summary>Weapon changes in the same frame as the piece are read after it; changes this close after the piece still count.</summary>
    private const float SameFrameToolSeconds = 0.02f;

    /// <summary>What a pawn held: the building tool, the editing tool or anything else.</summary>
    private enum HeldTool
    {
        Other,
        Build,
        Edit,
    }

    private sealed record PendingBuild(float T, int? Team, BuildMaterial Material, string Piece, FVector? Location, List<(ChannelGuid Pawn, double Distance)> NearbyPawns);

    /// <summary>Remembers material, piece type and location of a build actor when its channel opens.</summary>
    private void RegisterBuildChannel(ChannelIndex channelIndex, string? archetype, FVector? location)
    {
        _buildChannels.Remove(channelIndex);

        if (!_settings.TrackBuilds || archetype is null)
        {
            return;
        }

        System.Text.RegularExpressions.Match match = BuildArchetype.Match(archetype);
        if (!match.Success)
        {
            return;
        }

        BuildMaterial material = match.Groups["material"].Value switch
        {
            "W" => BuildMaterial.Wood,
            "S" => BuildMaterial.Stone,
            _ => BuildMaterial.Metal,
        };

        _buildChannels[channelIndex] = (material, match.Groups["piece"].Value, location);
    }

    /// <summary>
    /// Stores a new piece. Only the first update of a build actor counts, and only if the piece is still under construction
    /// (pieces that only become relevant later were built before). Edits also create a new piece under construction;
    /// they are told apart in <see cref="AssignBuilds"/> by the tool the pawns held, so here the pawns close to the piece are stored.
    /// </summary>
    private void TrackBuild(ChannelIndex channelIndex, PlayerBuild build)
    {
        if (!_buildChannels.TryGetValue(channelIndex, out (BuildMaterial Material, string Piece, FVector? Location) info) ||
            !_channelGuid.TryGetValue(channelIndex, out ChannelGuid buildGuid) ||
            !_seenBuilds.Add(buildGuid))
        {
            return;
        }

        if (build.bUnderConstruction != true && build.bIsInitiallyBuilding != true)
        {
            return;
        }

        List<(ChannelGuid Pawn, double Distance)> nearby = [];
        if (info.Location is { } location)
        {
            foreach ((ChannelGuid pawnGuid, FVector pawnLocation) in _pawnCurrentLocation)
            {
                if (!_liveGuidChannel.ContainsKey(pawnGuid))
                {
                    continue;
                }

                double dx = pawnLocation.X - location.X;
                double dy = pawnLocation.Y - location.Y;
                double dz = pawnLocation.Z - location.Z;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (distance <= MaxBuildDistance)
                {
                    nearby.Add((pawnGuid, distance));
                }
            }
        }

        _pendingBuilds.Add(new PendingBuild(CurrentTimeSeconds, build.TeamIndex, info.Material, info.Piece, info.Location, nearby));
    }

    /// <summary>Stores which kind of tool the pawn switched to (every weapon change, also to a non-tool).</summary>
    private void TrackToolChange(ChannelGuid pawnGuid, uint weaponGuid)
    {
        if (_settings.TrackBuilds)
        {
            AddToList(_pawnToolChanges, pawnGuid, (CurrentTimeSeconds, ToolOfWeapon(weaponGuid)));
        }
    }

    private HeldTool ToolOfWeapon(uint weaponGuid)
    {
        string? archetype = _guidArchetype.GetValueOrDefault(new ChannelGuid(weaponGuid));
        if (archetype is null)
        {
            return HeldTool.Other;
        }

        if (archetype.Contains("BuildingTool", StringComparison.Ordinal))
        {
            return HeldTool.Build;
        }

        return archetype.Contains("EditingTool", StringComparison.Ordinal)
            ? HeldTool.Edit
            : HeldTool.Other;
    }

    /// <summary>
    /// Tool the pawn held when a piece appeared at replay time <paramref name="t"/>. Changes in the same frame count
    /// (the pawn update is often read after the piece). If the pawn had just put a tool away, that tool counts:
    /// a piece is placed and the weapon switched in the same moment.
    /// </summary>
    private HeldTool ToolAt(ChannelGuid pawnGuid, float t)
    {
        if (!_pawnToolChanges.TryGetValue(pawnGuid, out List<(float T, HeldTool Tool)>? changes))
        {
            return HeldTool.Other;
        }

        int index = changes.FindLastIndex(c => c.T <= t + SameFrameToolSeconds);
        if (index < 0)
        {
            return HeldTool.Other;
        }

        (float changedAt, HeldTool tool) = changes[index];
        if (tool != HeldTool.Other || index == 0 || t - changedAt > ToolSwitchToleranceSeconds)
        {
            return tool;
        }

        return changes[index - 1].Tool;
    }

    /// <summary>
    /// Gives every new piece to the pawn of the piece's team that held the building tool (the replay does not say who built it).
    /// If several teammates held it, the nearest one gets it.
    /// A piece without a teammate holding the building tool is an edit (or cannot be attributed) and is not counted.
    /// </summary>
    private void AssignBuilds()
    {
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.RawBuilds.Clear();
        }

        foreach (PendingBuild build in _pendingBuilds)
        {
            PlayerStats? builder = null;
            double best = double.MaxValue;

            foreach ((ChannelGuid pawn, double distance) in build.NearbyPawns)
            {
                PlayerStats? player = PlayerOfPawn(pawn);
                if (player is null || (build.Team is not null && player.TeamIndex != build.Team))
                {
                    continue;
                }

                if (distance < best && ToolAt(pawn, build.T) == HeldTool.Build)
                {
                    builder = player;
                    best = distance;
                }
            }

            if (builder is not null)
            {
                builder.RawBuilds.Add(new BuildEvent(build.T, build.Material, build.Piece, build.Location));
            }
        }
    }
}

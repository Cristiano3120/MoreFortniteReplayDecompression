using FortniteReplayReader.Models;
using MoreFortniteReplayDecompression.StronglyTypedIds;

namespace MoreFortniteReplayDecompression;

/// <summary>Entry point: reads a replay and returns the standard result plus per-player statistics and (optionally) the typed timeline.</summary>
public static class ReplayExporter
{
    /// <param name="path">Path to the .replay file.</param>
    /// <param name="positionInterval">Seconds between two stored pawn positions per player.</param>
    /// <param name="collectTimeline">
    /// <c>true</c>: also store the full typed timeline (<see cref="ReplayExport.Timeline"/>), which costs time and memory.
    /// <c>false</c>: only the library result and <see cref="PlayerStats"/> (much leaner, enough for damage/health per player).
    /// </param>
    public static ReplayExport Read(string path, float positionInterval = 1f, bool collectTimeline = true)
    {
        return Read(path, new ExportSettings
        {
            PositionInterval = positionInterval,
            CollectTimeline = collectTimeline,
        });
    }

    /// <param name="path">Path to the .replay file.</param>
    /// <param name="settings">What should be read. Everything that is switched off is not parsed (faster).</param>
    public static ReplayExport Read(string path, ExportSettings settings)
    {
        FullExportReader reader = new(settings);
        FortniteReplay replay = reader.ReadReplay(path);
        List<PlayerStats> players = reader.BuildPlayers(replay);
        
        return new ReplayExport
        {
            Replay = replay,
            Timeline = reader.Events,
            GuidToChannel = new Dictionary<ChannelGuid, ChannelIndex>(reader.GuidToChannel),
            GuidToArchetype = new Dictionary<ChannelGuid, string?>(reader.GuidToArchetype),
            Players = [.. players.Where(p => !p.IsBot)],
            Bots = [.. players.Where(p => p.IsBot)],
            MatchStartTime = reader.MatchStartTime,
            StormPhases = reader.StormPhases,
            SupplyDrops = reader.SupplyDrops,
        };
    }
}

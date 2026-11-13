using FortniteReplayReader.Models;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.Reading;
using MoreFortniteReplayDecompression.StronglyTypedIds;

namespace MoreFortniteReplayDecompression.Export;

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
        if (settings.MetadataOnly)
        {
            FortniteReplay metadataReplay = MetadataReader.Read(path);
            return new ReplayExport
            {
                Replay = metadataReplay,
                Metadata = ReplayMetadata.From(metadataReplay),
                Timeline = [],
                GuidToChannel = [],
                GuidToArchetype = [],
                Players = [],
                Bots = [],
            };
        }

        FullExportReader reader = new(settings);
        FortniteReplay replay = reader.ReadReplay(path);
        List<PlayerStats> players = reader.BuildPlayers(replay);
        
        return new ReplayExport
        {
            Replay = replay,
            Metadata = ReplayMetadata.From(replay),
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

    /// <summary>
    /// Reads only the metadata of a replay (time, length, tournament, playlist, ...). Only the start of the replay is read,
    /// so this is much faster than a full read. Same as <see cref="Read(string, ExportSettings)"/> with <see cref="ExportSettings.Metadata"/>.
    /// </summary>
    /// <param name="path">Path to the .replay file.</param>
    public static ReplayMetadata ReadMetadata(string path) => ReplayMetadata.From(MetadataReader.Read(path));
}

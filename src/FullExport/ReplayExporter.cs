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
        var reader = new FullExportReader(positionInterval, collectTimeline: collectTimeline);
        var replay = reader.ReadReplay(path);
        var players = reader.BuildPlayers(replay);

        return new ReplayExport
        {
            Replay = replay,
            Timeline = reader.Events,
            GuidToChannel = new Dictionary<uint, uint>(reader.GuidToChannel),
            GuidToArchetype = new Dictionary<uint, string?>(reader.GuidToArchetype),
            Players = players,
            MatchStartTime = reader.MatchStartTime,
        };
    }
}

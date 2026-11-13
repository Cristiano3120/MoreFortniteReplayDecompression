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

    /// <summary>
    /// Reads several replays at the same time and returns one result per path, in the order of <paramref name="paths"/>.
    /// A replay that cannot be read does not stop the others (see <see cref="ParallelReadOptions.ContinueOnError"/>).
    /// <para>
    /// Tip: switch on the server garbage collector in the project of your program
    /// (<c>&lt;ServerGarbageCollection&gt;true&lt;/ServerGarbageCollection&gt;</c> in the .csproj). Reading in parallel creates a lot of
    /// garbage on many threads; with the server garbage collector 40 replays took 12 s instead of 22 s on 16 cores.
    /// A library cannot switch it on for the program.
    /// </para>
    /// </summary>
    /// <param name="paths">Paths to the .replay files.</param>
    /// <param name="settings">What should be read from every replay, default <see cref="ExportSettings.StatsOnly"/>.</param>
    /// <param name="options">How the replays are read in parallel, see <see cref="ParallelReadOptions"/>.</param>
    /// <exception cref="OperationCanceledException">The <see cref="ParallelReadOptions.CancellationToken"/> was cancelled.</exception>
    /// <exception cref="Exception">
    /// Only with <see cref="ParallelReadOptions.ContinueOnError"/> = <c>false</c>: the error of the replay that failed
    /// (an <see cref="AggregateException"/> if several failed at the same time). Also an exception thrown in <see cref="ParallelReadOptions.OnReplayRead"/>.
    /// </exception>
    public static List<ReplayReadResult> ReadMany(IEnumerable<string> paths, ExportSettings? settings = null, ParallelReadOptions? options = null)
    {
        settings ??= ExportSettings.StatsOnly;
        options ??= new ParallelReadOptions();

        List<string> files = [.. paths];
        var results = new ReplayReadResult[files.Count];

        // Indexes into files, so every result lands at the position of its path whatever the reading order.
        IEnumerable<int> order = Enumerable.Range(0, files.Count);
        if (options.LargestFirst)
        {
            order = order.OrderByDescending(i => FileSize(files[i]));
        }

        ParallelOptions parallel = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelReplays),
            CancellationToken = options.CancellationToken,
        };

        try
        {
            Parallel.ForEach(order, parallel, index => results[index] = ReadOne(files[index], settings, options));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Throw the error itself instead of the wrapper of Parallel.ForEach.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }

        return [.. results];
    }

    private static ReplayReadResult ReadOne(string path, ExportSettings settings, ParallelReadOptions options)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        ReplayReadResult result;

        try
        {
            ReplayExport export = Read(path, settings);
            result = new ReplayReadResult { Path = path, Export = export, Duration = started.Elapsed };
        }
        catch (Exception ex) when (options.ContinueOnError)
        {
            result = new ReplayReadResult { Path = path, Error = ex, Duration = started.Elapsed };
        }

        options.OnReplayRead?.Invoke(result);

        return options.KeepResults || result.Export is null
            ? result
            : new ReplayReadResult { Path = path, Duration = result.Duration };
    }

    /// <summary>Size of a file, 0 if it does not exist (reading it then fails with a proper error).</summary>
    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }
}

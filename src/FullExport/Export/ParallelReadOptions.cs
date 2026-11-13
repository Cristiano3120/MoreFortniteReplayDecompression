namespace MoreFortniteReplayDecompression.Export;

/// <summary>Settings for reading several replays at the same time with <see cref="ReplayExporter.ReadMany"/>.</summary>
public sealed class ParallelReadOptions
{
    /// <summary>
    /// How many replays are read at the same time. Default: the number of logical CPU cores.
    /// Every replay that is read at the same time is in memory at the same time (a server replay with a timeline can need
    /// more than 1 GB), so lower this if the memory runs out. Half of the cores was only ~20 % slower and needed ~15 % less memory.
    /// </summary>
    public int MaxParallelReplays { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// <c>true</c> (default): start with the largest files. A large file started last would keep running alone at the end
    /// while all other cores are idle. The results are still returned in the order of the given paths.
    /// </summary>
    public bool LargestFirst { get; init; } = true;

    /// <summary>
    /// Your own function that <see cref="ReplayExporter.ReadMany"/> calls once for every replay, right after that replay is read,
    /// while the other replays are still being read. It is also called for a replay that could not be read
    /// (then <see cref="ReplayReadResult.Error"/> is set and <see cref="ReplayReadResult.Export"/> is <c>null</c>).
    /// <c>ReadMany</c> returns only after it was called for all replays.
    /// <para>
    /// It is called from several threads at the same time, so the code in it must be thread-safe
    /// (for example use <c>lock</c> when adding up values). Optional: without it you just get all results at the end.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// double damage = 0;
    /// object sync = new();
    /// ReplayExporter.ReadMany(paths, options: new ParallelReadOptions
    /// {
    ///     KeepResults = false,
    ///     OnReplayRead = result =>
    ///     {
    ///         PlayerStats? player = result.Export?.Player("Detect Bergii yh");
    ///         lock (sync) damage += player?.DamageDealt ?? 0;
    ///     },
    /// });
    /// </code>
    /// </example>
    public Action<ReplayReadResult>? OnReplayRead { get; init; }

    /// <summary>
    /// Whether <see cref="ReplayExporter.ReadMany"/> keeps every read replay until it is done and returns it.
    /// <list type="bullet">
    /// <item>
    /// <c>true</c> (default): all read replays stay in memory until <c>ReadMany</c> returns, and the returned results contain them
    /// (<see cref="ReplayReadResult.Export"/>). Convenient, but 40 replays needed 7.7 GB.
    /// </item>
    /// <item>
    /// <c>false</c>: every replay is only passed to <see cref="OnReplayRead"/> and dropped right after it, so only the replays that
    /// are currently being read are in memory (40 replays: 3.3 GB). In the returned results <see cref="ReplayReadResult.Export"/>
    /// is <c>null</c>; they still have the path, the error and the duration. Use this for many or large replays and take what you
    /// need in <see cref="OnReplayRead"/>.
    /// </item>
    /// </list>
    /// </summary>
    public bool KeepResults { get; init; } = true;

    /// <summary>
    /// <c>true</c> (default): a replay that cannot be read is returned with <see cref="ReplayReadResult.Error"/> and the others go on.
    /// <c>false</c>: the first error stops the reading of the replays that did not start yet and is thrown.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>Stops starting new replays. Replays that are already being read are finished first.</summary>
    public CancellationToken CancellationToken { get; init; }
}

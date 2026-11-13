namespace MoreFortniteReplayDecompression.Export;

/// <summary>Result of one replay read by <see cref="ReplayExporter.ReadMany"/>.</summary>
public sealed class ReplayReadResult
{
    /// <summary>Path of the replay file, as it was given.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// The read replay. <c>null</c> if it could not be read (see <see cref="Error"/>), or if <see cref="ParallelReadOptions.KeepResults"/>
    /// is off and this result is the returned one (the callback <see cref="ParallelReadOptions.OnReplayRead"/> still gets it).
    /// </summary>
    public ReplayExport? Export { get; init; }

    /// <summary>Why the replay could not be read, <c>null</c> if it was read.</summary>
    public Exception? Error { get; init; }

    /// <summary>The replay was read without an error.</summary>
    public bool Success => Error is null;

    /// <summary>How long reading this replay took.</summary>
    public TimeSpan Duration { get; init; }
}

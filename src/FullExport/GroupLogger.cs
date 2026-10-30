using Microsoft.Extensions.Logging;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// Optional diagnostic logger. Collects the export groups (class paths) that occur in the replay but are not read by the library.
/// Use it to discover new data: for every such class you can add your own <c>[NetFieldExportGroup]</c> class.
/// Pass it as <c>logger</c> to <see cref="FullExportReader"/>.
/// </summary>
public sealed class GroupLogger : ILogger
{
    /// <summary>Class path to number of times it was skipped.</summary>
    public SortedDictionary<string, int> NotRead { get; } = new(StringComparer.Ordinal);

    /// <summary>Error messages logged by the library, with counts.</summary>
    public SortedDictionary<string, int> Errors { get; } = new(StringComparer.Ordinal);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (message.StartsWith("Not reading type ", StringComparison.Ordinal))
        {
            var path = message["Not reading type ".Length..];
            NotRead[path] = NotRead.GetValueOrDefault(path) + 1;
        }
        else if (logLevel >= LogLevel.Error)
        {
            var key = message.Length > 160 ? message[..160] : message;
            Errors[key] = Errors.GetValueOrDefault(key) + 1;
        }
    }
}

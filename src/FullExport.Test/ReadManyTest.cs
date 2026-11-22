using System.Collections.Concurrent;
using MoreFortniteReplayDecompression.Export;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

/// <summary><see cref="ReplayExporter.ReadMany"/> with files that are not replays, so every read fails.</summary>
public sealed class ReadManyTest : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("FullExportTest").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Files that are not replays, of different sizes (so <see cref="ParallelReadOptions.LargestFirst"/> changes the order).</summary>
    private List<string> BrokenFiles(int count)
    {
        List<string> paths = [];
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(_dir, $"broken{i}.replay");
            File.WriteAllBytes(path, new byte[(i + 1) * 100]);
            paths.Add(path);
        }

        return paths;
    }

    [Fact]
    public void ResultsKeepThePathOrder()
    {
        List<string> paths = [.. BrokenFiles(5), Path.Combine(_dir, "missing.replay")];

        List<ReplayReadResult> results = ReplayExporter.ReadMany(paths, options: new ParallelReadOptions { MaxParallelReplays = 3, LargestFirst = true });

        Assert.Equal(paths, results.Select(r => r.Path));
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
            Assert.Null(r.Export);
        });
    }

    [Fact]
    public void CallbackRunsOncePerReplay()
    {
        List<string> paths = BrokenFiles(4);
        ConcurrentBag<string> called = [];

        ReplayExporter.ReadMany(paths, options: new ParallelReadOptions { OnReplayRead = r => called.Add(r.Path) });

        Assert.Equal(paths.Order(), called.Order());
    }

    [Fact]
    public void WithoutContinueOnErrorTheErrorIsThrown()
    {
        List<string> paths = BrokenFiles(1);

        Exception? ex = Record.Exception(() => ReplayExporter.ReadMany(paths, options: new ParallelReadOptions { ContinueOnError = false }));

        Assert.NotNull(ex);
        Assert.IsNotType<AggregateException>(ex);
    }

    [Fact]
    public void CancelledTokenStopsReading()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ReplayExporter.ReadMany(BrokenFiles(2), options: new ParallelReadOptions { CancellationToken = cts.Token }));
    }

    [Fact]
    public void NoPathsGiveNoResults()
    {
        Assert.Empty(ReplayExporter.ReadMany([]));
    }
}

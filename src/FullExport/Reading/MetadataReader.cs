using System.Reflection;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models.Enums;

namespace MoreFortniteReplayDecompression.Reading;

/// <summary>
/// Reads only what <see cref="Export.ReplayMetadata"/> needs and stops as soon as it has it.
/// <list type="bullet">
/// <item>The file is mapped into memory instead of being read, so only the parts that are processed (the info section and the first chunks) are loaded from disk.</item>
/// <item>Everything except the game state and the playlist is skipped by the parser.</item>
/// <item>Once the values are known, reading stops, even in the middle of a data chunk; the rest of the file is never loaded or decompressed.</item>
/// </list>
/// In the checked replays everything arrives with the first game state, right at the start of the first data chunk.
/// </summary>
internal sealed class MetadataReader : FortniteReplayReader.ReplayReader
{
    /// <summary>Stop at the latest this many seconds after the first game state, even if the playlist never arrives.</summary>
    private const float MaxWaitSeconds = 60f;

    // All export groups of the library except the ones the metadata comes from: the parser skips their data.
    private static readonly IReadOnlySet<Type> SkippedGroups = typeof(GameState).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<NetFieldExportGroupAttribute>() is not null && t != typeof(GameState) && t != typeof(PlaylistInfo))
        .ToHashSet();

    private int _length;

    private float? _firstGameState;
    private bool _hasPlaylist;
    private bool _done;

    public MetadataReader() : base(null!, ParseMode.Minimal, SkippedGroups)
    {
    }

    // Creating a reader sets up the parser for all export groups (reflection), which takes ~2 ms, about a fifth of a metadata read.
    // A reader resets itself after every replay, so one reader per thread is reused.
    [ThreadStatic]
    private static MetadataReader? _reusable;

    /// <summary>Reads the metadata of the replay at <paramref name="path"/> with a reused reader of the current thread.</summary>
    public static FortniteReplay Read(string path) => (_reusable ??= new MetadataReader()).ReadMetadata(path);

    /// <summary>Reads the metadata of the replay at <paramref name="path"/>, loading only the needed parts of the file.</summary>
    public FortniteReplay ReadMetadata(string path)
    {
        // State of the previous replay. Everything else is reset by the reader itself after each replay.
        _firstGameState = null;
        _hasPlaylist = false;
        _done = false;
        _netGuidCache.ClearExportGroups();

        using MappedFileMemory file = new(path);
        _length = file.Memory.Length;
        return ReadReplay(file.Memory);
    }

    /// <summary>Same as the base loop, but skips checkpoints and stops as soon as the metadata is complete.</summary>
    public override void ReadReplayChunks(FArchive archive)
    {
        while (!archive.AtEnd() && !_done)
        {
            var chunkType = archive.ReadUInt32AsEnum<ReplayChunkType>();
            var chunkSize = archive.ReadInt32();
            var offset = archive.Position;

            if (chunkSize <= 0 || (long)chunkSize + offset > _length)
            {
                archive.SetError();
                return;
            }

            // Checkpoints are not needed (the base reader skips them as well).
            if (chunkType is ReplayChunkType.Header or ReplayChunkType.ReplayData or ReplayChunkType.Event)
            {
                switch (chunkType)
                {
                    case ReplayChunkType.Header:
                        ReadReplayHeader(archive);
                        break;
                    case ReplayChunkType.ReplayData:
                        ReadReplayData(archive, chunkSize);
                        break;
                    case ReplayChunkType.Event:
                        ReadEvent(archive);
                        break;
                }
            }

            archive.Seek(offset + chunkSize, SeekOrigin.Begin);
        }
    }

    /// <summary>Stops in the middle of a data chunk as soon as the metadata is complete: the remaining frames are not read.</summary>
    public override void ReadDemoFrameIntoPlaybackPackets(FArchive archive)
    {
        if (_done)
        {
            archive.Seek(0, SeekOrigin.End);
            return;
        }

        base.ReadDemoFrameIntoPlaybackPackets(archive);
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);

        switch (exportGroup)
        {
            case GameState:
                _firstGameState ??= CurrentTimeSeconds;
                break;

            case PlaylistInfo:
                _hasPlaylist = true;
                break;
        }

        if (_firstGameState is { } first)
        {
            // The first game state has all values (time, tournament, code name); the playlist follows in the same frame.
            _done = _hasPlaylist || CurrentTimeSeconds - first > MaxWaitSeconds;
        }
    }
}

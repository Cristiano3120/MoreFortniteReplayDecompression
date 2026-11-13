using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace MoreFortniteReplayDecompression.Reading;

/// <summary>
/// A file mapped into memory, exposed as <see cref="Memory{T}"/>. Nothing is copied: the operating system only loads the pages
/// that are actually read, so reading the start of a large replay does not touch the rest of the file.
/// </summary>
internal sealed unsafe class MappedFileMemory : MemoryManager<byte>
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _pointer;
    private readonly int _length;

    public MappedFileMemory(string path)
    {
        long length = new FileInfo(path).Length;
        if (length > int.MaxValue)
        {
            throw new NotSupportedException($"Replays larger than {int.MaxValue} bytes are not supported.");
        }

        _length = (int)length;
        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _pointer = pointer + _view.PointerOffset;
    }

    public override Span<byte> GetSpan() => new(_pointer, _length);

    public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}

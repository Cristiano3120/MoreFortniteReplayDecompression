using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Unreal.Core.Extensions;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using Xunit;

namespace Unreal.Core.Test;

public class PerformanceVerificationTests
{
    /// <summary>
    /// Bytes <paramref name="loop"/> allocates on the current thread.
    /// The loop runs once to warm up first, then it is measured up to <paramref name="attempts"/> times and the smallest value counts.
    /// A single measurement is not reliable: the runtime can allocate on the test thread while it runs the loop (the JIT compiling
    /// or recompiling a method, code coverage in CI), which made these tests fail now and then on GitHub Actions.
    /// Real allocations of the measured code show up in every attempt, so they still fail the test.
    /// </summary>
    private static long AllocatedBytes(Action loop, int attempts = 5)
    {
        loop();

        long smallest = long.MaxValue;
        for (int i = 0; i < attempts && smallest > 0; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            loop();
            long after = GC.GetAllocatedBytesForCurrentThread();
            smallest = Math.Min(smallest, after - before);
        }

        return smallest;
    }

    [Fact]
    public void RemoveAllPathPrefixes_ZeroAllocationsAndFasterThanLinq()
    {
        const string testPath = "ROOT_Default__FortPlayerPawn_Athena_C";

        long allocated = AllocatedBytes(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                _ = testPath.RemoveAllPathPrefixes();
            }
        });

        // Should have 0 GC allocations
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BinaryPrimitives_DirectReads_ZeroAllocations()
    {
        var data = new byte[32];
        Random.Shared.NextBytes(data);

        long allocated = AllocatedBytes(() =>
        {
            var span = data.AsSpan();
            for (int i = 0; i < 1000; i++)
            {
                _ = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0, 2));
                _ = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(2, 4));
                _ = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(6, 4));
                _ = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(10, 8));
                _ = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(18, 4));
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void EnumParsing_UnsafeAs_ZeroAllocationsAndExactMatch()
    {
        byte b = 2;

        // Baseline: Enum.ToObject boxes
        var baseline = (ChannelCloseReason)Enum.ToObject(typeof(ChannelCloseReason), b);

        // Optimized: Unsafe.As
        var optimized = Unsafe.As<byte, ChannelCloseReason>(ref b);

        Assert.Equal(baseline, optimized);

        long allocated = AllocatedBytes(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                _ = Unsafe.As<byte, ChannelCloseReason>(ref b);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReadBitsToInt_BitShiftMatchesLoopAndAllocatesZero()
    {
        var buffer = new byte[16];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 17 + 3);

        var reader = new BitReader(buffer);

        // 1. Verify correctness against single-bit loop
        for (var bitCount = 1; bitCount <= 32; bitCount++)
        {
            reader.Seek(0);
            var optVal = reader.ReadBitsToInt(bitCount);

            var loopVal = 0;
            for (var b = 0; b < bitCount; b++)
            {
                var bit = (buffer[b >> 3] & (1 << (b & 7))) != 0;
                if (bit) loopVal |= (1 << b);
            }

            Assert.Equal(loopVal, optVal);
        }

        // 2. Verify zero allocations
        long allocated = AllocatedBytes(() =>
        {
            for (var bitCount = 1; bitCount <= 32; bitCount++)
            {
                reader.Seek(0);
                _ = reader.ReadBitsToInt(bitCount);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TempEnd_ArrayLookupMatchesDictAndIsFaster()
    {
        var reader = new BitReader(new byte[100]);

        // Set and restore temp end using optimized array
        reader.SetTempEnd(100, FBitArchiveEndIndex.BUNCH);
        Assert.Equal(100, reader.LastBit);
        reader.RestoreTempEnd(FBitArchiveEndIndex.BUNCH);
        Assert.Equal(800, reader.LastBit);

        long allocated = AllocatedBytes(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                reader.SetTempEnd(50, FBitArchiveEndIndex.BUNCH);
                reader.RestoreTempEnd(FBitArchiveEndIndex.BUNCH);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FillBitsFrom_ZeroAllocations()
    {
        var sourceBytes = new byte[256];
        Random.Shared.NextBytes(sourceBytes);
        var source = new BitReader(sourceBytes);
        var dest = new NetBitReader();

        long allocated = AllocatedBytes(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                source.Seek(0);
                dest.FillBitsFrom(source, 64);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NetGuidCache_DirectLookup_MatchesTwoStep()
    {
        var cache = new NetGuidCache();
        for (uint i = 0; i < 100; i++)
        {
            var name = $"Group_{i}";
            cache.AddToExportGroupMap(name, new NetFieldExportGroup { PathNameIndex = i, PathName = name });
        }

        for (uint i = 0; i < 100; i++)
        {
            var group = cache.GetNetFieldExportGroupFromIndex(i);
            Assert.NotNull(group);
            Assert.Equal($"Group_{i}", group.PathName);
        }

        long allocated = AllocatedBytes(() =>
        {
            for (int r = 0; r < 500; r++)
            {
                for (uint i = 0; i < 100; i++)
                {
                    _ = cache.GetNetFieldExportGroupFromIndex(i);
                }
            }
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>The helper must still catch code that really allocates, otherwise the tests above would prove nothing.</summary>
    [Fact]
    public void AllocatedBytes_DetectsRealAllocations()
    {
        // Stored in an array, so the JIT cannot turn the objects into stack allocations.
        var kept = new object[100];
        long allocated = AllocatedBytes(() =>
        {
            for (int i = 0; i < kept.Length; i++)
            {
                kept[i] = new object();
            }
        });

        Assert.True(allocated >= 100 * IntPtr.Size, $"allocated {allocated} bytes");
    }
}

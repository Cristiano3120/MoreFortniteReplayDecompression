using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using Xunit;

namespace Unreal.Core.Test;

/// <summary>
/// Gameplay cue data as current Fortnite replays write it (dynamic tags, Fortnite effect context, prediction key without base key).
/// </summary>
public class GameplayCueSerializationTest
{
    /// <summary>Writes bits the way Unreal reads them (least significant bit of each byte first).</summary>
    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public BitWriter Bit(bool value)
        {
            _bits.Add(value);
            return this;
        }

        public BitWriter Bits(uint value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _bits.Add(((value >> i) & 1) != 0);
            }
            return this;
        }

        /// <summary>FArchive::SerializeIntPacked: 7 value bits per byte, the lowest bit says whether another byte follows.</summary>
        public BitWriter IntPacked(uint value)
        {
            do
            {
                uint next = value >> 7;
                Bit(next != 0);
                Bits(value & 0x7F, 7);
                value = next;
            } while (value != 0);
            return this;
        }

        public NetBitReader Reader()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
            {
                if (_bits[i])
                {
                    bytes[i / 8] |= (byte)(1 << (i % 8));
                }
            }
            return new NetBitReader(bytes, _bits.Count) { EngineNetworkVersion = EngineNetworkVersionHistory.LATEST };
        }
    }

    [Fact]
    public void DynamicTagIsMarkedAsDynamic()
    {
        NetBitReader reader = new BitWriter().Bit(false).Bit(true).IntPacked(204).Bit(true).Reader();
        FGameplayTag tag = new(reader);

        Assert.True(tag.IsDynamic);
        Assert.Equal(204u, tag.TagIndex);
        Assert.True(reader.AtEnd());
        Assert.False(reader.IsError);
    }

    [Fact]
    public void FastTagIsNotDynamic()
    {
        NetBitReader reader = new BitWriter().Bit(true).IntPacked(17).Reader();
        FGameplayTag tag = new(reader);

        Assert.False(tag.IsDynamic);
        Assert.Equal(17u, tag.TagIndex);
        Assert.True(reader.AtEnd());
    }

    [Fact]
    public void PredictionKeyWithoutBaseKey()
    {
        NetBitReader reader = new BitWriter().Bit(true).Bit(false).Bits(760, 16).Reader();
        FPredictionKey key = new();
        key.Serialize(reader);

        Assert.Equal(760, key.CurrentKey);
        Assert.True(reader.AtEnd());
        Assert.False(reader.IsError);
    }

    [Fact]
    public void ParametersWithFortniteEffectContext()
    {
        BitWriter writer = new BitWriter()
            .Bits(1 << (int)RepFlag.REP_EffectContext, (int)RepFlag.REP_MAX)
            .Bit(true).Bit(true)            // source and target tags empty
            .Bit(true)                      // context valid
            .IntPacked(1)                   // script struct
            .Bits(0b0000011, 7)             // instigator and effect causer
            .IntPacked(15842)
            .IntPacked(33092)
            .Bits(0, 32).Bits(0, 32).Bits(0b1000000, 7); // Fortnite specific part of the context (71 bits)

        NetBitReader reader = writer.Reader();
        FGameplayCueParameters parameters = new();
        parameters.Serialize(reader);

        Assert.NotNull(parameters.EffectContext);
        Assert.True(parameters.EffectContext.IsValid);
        Assert.Equal(1u, parameters.EffectContext.ScriptStruct);
        Assert.Equal(15842u, parameters.EffectContext.Instigator);
        Assert.Equal(33092u, parameters.EffectContext.EffectCauser);
        Assert.True(reader.AtEnd());
        Assert.False(reader.IsError);
    }

    [Fact]
    public void ParametersWithRawMagnitudeOnly()
    {
        NetBitReader reader = new BitWriter()
            .Bits(1 << (int)RepFlag.REP_RawMagnitude, (int)RepFlag.REP_MAX)
            .Bit(true).Bit(true)
            .Bits(BitConverter.SingleToUInt32Bits(42.5f), 32)
            .Reader();
        FGameplayCueParameters parameters = new();
        parameters.Serialize(reader);

        Assert.Equal(42.5f, parameters.RawMagnitude);
        Assert.Null(parameters.EffectContext);
        Assert.True(reader.AtEnd());
    }
}

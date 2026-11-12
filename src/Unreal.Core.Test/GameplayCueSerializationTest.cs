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
        Assert.Equal(71, parameters.EffectContext.GameSpecificBits);
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

    /// <summary>Effect context of a projectile hit on a building, taken from a Fortnite replay (hit result, world origin, Fortnite data).</summary>
    private static readonly byte[] ContextWithHitResult =
    [
        0x05, 0xDE, 0xB3, 0x9A, 0xB3, 0x9A, 0xD3, 0x9C, 0xDB, 0x9C, 0x78, 0xAA, 0x2D, 0x40, 0x3F, 0x50,
        0xCE, 0x33, 0x5E, 0x42, 0x23, 0x02, 0xB5, 0x88, 0x8E, 0xA3, 0x24, 0x4F, 0x28, 0xE7, 0x19, 0x2F,
        0xA1, 0x11, 0x01, 0xF4, 0xE8, 0x0C, 0x92, 0xB0, 0x92, 0x00, 0x9A, 0x7B, 0xA6, 0x4C, 0xB8, 0x42,
        0xB0, 0xE3, 0x96, 0xFD, 0x40, 0xF0, 0xFF, 0xFF, 0xFF, 0x0F, 0xD0, 0x58, 0x47, 0x10, 0x00, 0x20,
        0x19, 0x85, 0xDD, 0x5D, 0x37, 0x3A, 0x1B, 0xC8, 0xBD, 0xC7, 0x76, 0xC8, 0x82, 0xDB, 0x1B, 0x78,
        0x74, 0x07, 0xB1, 0x33, 0x49, 0x4A, 0x12, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x08
    ];

    [Fact]
    public void EffectContextWithHitResult()
    {
        NetBitReader reader = new(ContextWithHitResult, 772) { EngineNetworkVersion = EngineNetworkVersionHistory.LATEST };
        FGameplayEffectContextHandle context = new();
        context.Serialize(reader);

        Assert.True(context.IsValid);
        Assert.Equal(9945u, context.Instigator);
        Assert.Equal(9945u, context.EffectCauser);
        Assert.Equal(10089u, context.AbilityCDO);
        Assert.Equal(10093u, context.SourceObject);

        Assert.NotNull(context.HitResult);
        Assert.Equal(33772u, context.HitResult.Actor);
        Assert.Equal(-1, context.HitResult.InstanceIndex);
        Assert.Equal(40262u, context.HitResult.Component);
        Assert.Equal("None", context.HitResult.BoneName);
        Assert.Equal(26524, context.HitResult.Location.X, 0.5);
        Assert.Equal(-31556, context.HitResult.Location.Y, 0.5);

        Assert.True(context.bHasWorldOrigin);
        Assert.Equal(26438.92, context.WorldOrigin.X, 0.01);
        Assert.Equal(71, context.GameSpecificBits);
        Assert.True(reader.AtEnd());
        Assert.False(reader.IsError);
    }
}

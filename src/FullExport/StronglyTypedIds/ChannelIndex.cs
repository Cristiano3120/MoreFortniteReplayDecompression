namespace MoreFortniteReplayDecompression.StronglyTypedIds;

public readonly record struct ChannelIndex(uint Value)
{
    public static bool operator ==(ChannelIndex left, uint right)
    {
        return left.Value == right;
    }

    public static bool operator !=(ChannelIndex left, uint right)
    {
        return left.Value != right;
    }
}

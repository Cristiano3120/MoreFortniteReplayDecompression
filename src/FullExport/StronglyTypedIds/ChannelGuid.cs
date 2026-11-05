namespace MoreFortniteReplayDecompression.StronglyTypedIds;

public readonly record struct ChannelGuid(uint Value)
{
    public static bool operator == (ChannelGuid left, uint right)
    {
        return left.Value == right;
    }

    public static bool operator !=(ChannelGuid left, uint right)
    {
        return left.Value != right;
    }
}

using MoreFortniteReplayDecompression.Players;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

public class ItemIdsTest
{
    [Theory]
    [InlineData("WID_Shotgun_Break_Flintlock_Athena_SR", ItemRarity.Legendary)]
    [InlineData("WID_Assault_Flintlock_Athena_VR", ItemRarity.Epic)]
    [InlineData("WID_Assault_Flintlock_Athena_R", ItemRarity.Rare)]
    [InlineData("WID_Assault_Flintlock_Athena_UC", ItemRarity.Uncommon)]
    [InlineData("WID_Assault_Heavy_Recoil_Athena_C", ItemRarity.Common)]
    [InlineData("WID_Delilah_Slingshot_HarmlessThrift_Athena_UR", ItemRarity.Mythic)]
    [InlineData("WID_Pistol_Scavenger_Athena_UC_Ore_T03", ItemRarity.Uncommon)]
    [InlineData("Athena_PurpleStuff_VR", ItemRarity.Epic)]
    [InlineData("WID_Sniper_Example_Athena_Exotic", ItemRarity.Exotic)]
    [InlineData("WID_Sniper_Example_Exotic_Athena_SR", ItemRarity.Exotic)]
    public void RarityComesFromTheIdSuffix(string id, ItemRarity rarity)
    {
        Assert.Equal(rarity, ItemIds.RarityOf(id));
    }

    [Theory]
    [InlineData("WID_Paprika_ShieldPot")]
    [InlineData("WID_Athena_Flopper")]
    [InlineData("")]
    [InlineData(null)]
    public void ItemsWithoutRarityCodeHaveNone(string? id)
    {
        Assert.Null(ItemIds.RarityOf(id));
    }

    [Theory]
    [InlineData("WID_Shotgun_Break_Flintlock_Athena_SR", "WID_Shotgun_Break_Flintlock_Athena")]
    [InlineData("WID_Shotgun_Break_Flintlock_Athena_UC", "WID_Shotgun_Break_Flintlock_Athena")]
    [InlineData("WID_Pistol_Scavenger_Athena_UC_Ore_T03", "WID_Pistol_Scavenger_Athena")]
    [InlineData("WID_Paprika_ShieldPot", "WID_Paprika_ShieldPot")]
    public void BaseIdIsTheSameForEveryRarity(string id, string baseId)
    {
        Assert.Equal(baseId, ItemIds.BaseId(id));
    }
}

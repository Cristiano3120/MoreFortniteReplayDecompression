namespace MoreFortniteReplayDecompression.Players;

/// <summary>Rarity of an item, read from the end of its item id (for example <c>WID_Shotgun_Break_Flintlock_Athena_SR</c> = Legendary).</summary>
public enum ItemRarity
{
    /// <summary>Grey, item id ends with <c>_C</c>.</summary>
    Common,

    /// <summary>Green, item id ends with <c>_UC</c>.</summary>
    Uncommon,

    /// <summary>Blue, item id ends with <c>_R</c>.</summary>
    Rare,

    /// <summary>Purple, item id ends with <c>_VR</c> ("very rare").</summary>
    Epic,

    /// <summary>Gold, item id ends with <c>_SR</c> ("super rare").</summary>
    Legendary,

    /// <summary>Item id ends with <c>_UR</c>.</summary>
    Mythic,

    /// <summary>
    /// Exotic, recognized by an <c>Exotic</c> part in the item id. Not seen in the test replays yet, so the exact id format of
    /// exotic weapons is not confirmed; an exotic weapon with another id format shows up with its other rarity code or <c>null</c>.
    /// </summary>
    Exotic,
}

/// <summary>Helpers for item ids such as <c>WID_Shotgun_Break_Flintlock_Athena_SR</c>.</summary>
public static class ItemIds
{
    /// <summary>
    /// Rarity from the item id, <c>null</c> if the id has no rarity part (most consumables, for example <c>WID_Paprika_ShieldPot</c>).
    /// The rarity is the last part of the id that is a rarity code; parts after it (<c>_Ore_T03</c>, <c>_T01</c>) are ignored.
    /// </summary>
    public static ItemRarity? RarityOf(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        string[] parts = itemId.Split('_');
        if (parts.Any(p => p.Equals("Exotic", StringComparison.OrdinalIgnoreCase)))
        {
            return ItemRarity.Exotic;
        }

        for (int i = parts.Length - 1; i > 0; i--)
        {
            ItemRarity? rarity = parts[i] switch
            {
                "C" => ItemRarity.Common,
                "UC" => ItemRarity.Uncommon,
                "R" => ItemRarity.Rare,
                "VR" => ItemRarity.Epic,
                "SR" => ItemRarity.Legendary,
                "UR" => ItemRarity.Mythic,
                _ => null,
            };

            if (rarity is not null)
            {
                return rarity;
            }
        }

        return null;
    }

    /// <summary>
    /// The item id without its rarity and the parts after it: <c>WID_Shotgun_Break_Flintlock_Athena_SR</c> ->
    /// <c>WID_Shotgun_Break_Flintlock_Athena</c>. The same weapon in every rarity has the same base id, so this is the key for a
    /// table of display names. Ids without a rarity are returned unchanged.
    /// </summary>
    public static string BaseId(string itemId)
    {
        string[] parts = itemId.Split('_');
        for (int i = parts.Length - 1; i > 0; i--)
        {
            if (parts[i] is "C" or "UC" or "R" or "VR" or "SR" or "UR")
            {
                return string.Join('_', parts, 0, i);
            }
        }

        return itemId;
    }
}

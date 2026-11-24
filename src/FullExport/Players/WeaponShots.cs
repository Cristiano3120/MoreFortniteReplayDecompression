namespace MoreFortniteReplayDecompression.Players;

/// <summary>Shots and hits of one player with one weapon class, see <see cref="PlayerStats.WeaponShots"/>.</summary>
public sealed class WeaponShots
{
    /// <summary>Class (archetype path) of the weapon, for example <c>B_Assault_Flintlock_C</c>.</summary>
    public required string Weapon { get; init; }

    /// <summary>
    /// Shots fired with this weapon: one per bullet, shell or projectile (one per ammo used). A burst counts every bullet,
    /// a shotgun shot counts once (not per pellet), a slingshot shot once.
    /// </summary>
    public int ShotsFired { get; init; }

    /// <summary>Hits on other players with this weapon (see <see cref="PlayerStats.AccuracyHits"/>).</summary>
    public int Hits { get; init; }

    /// <summary><see cref="Hits"/> / <see cref="ShotsFired"/>, <c>null</c> without shots. Can be above 1 when one shot hits several players.</summary>
    public float? Accuracy => ShotsFired > 0 ? (float)Hits / ShotsFired : null;

    /// <summary>
    /// The same split by item, so by rarity (for example a green and a gold version of this weapon), most shots first.
    /// Empty without <see cref="Export.ExportSettings.TrackWeaponItems"/>. Shots and hits of weapons whose item is unknown are
    /// only in the totals above, so the items can add up to less.
    /// </summary>
    public List<WeaponItemShots> Items { get; init; } = [];
}

/// <summary>Shots and hits of one player with one item (weapon in one rarity), see <see cref="WeaponShots.Items"/>.</summary>
public sealed class WeaponItemShots
{
    /// <summary>Item id, for example <c>WID_Shotgun_Break_Flintlock_Athena_SR</c>.</summary>
    public required string Item { get; init; }

    /// <summary>Rarity from <see cref="Item"/>, <c>null</c> if the item has no rarity.</summary>
    public ItemRarity? Rarity => ItemIds.RarityOf(Item);

    public int ShotsFired { get; init; }
    public int Hits { get; init; }

    /// <summary><see cref="Hits"/> / <see cref="ShotsFired"/>, <c>null</c> without shots.</summary>
    public float? Accuracy => ShotsFired > 0 ? (float)Hits / ShotsFired : null;
}

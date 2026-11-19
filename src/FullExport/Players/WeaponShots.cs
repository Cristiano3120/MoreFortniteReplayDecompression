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
}

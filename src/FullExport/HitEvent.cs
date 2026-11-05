using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>
/// A single hit taken from the <c>BatchedDamageCues</c> RPC of the replay (attacker, target, damage, flags).
/// Only hits on players are stored as events; hits on structures and other objects are only summed up in <see cref="PlayerStats"/>.
/// </summary>
public sealed class HitEvent
{
    /// <summary>Replay time in seconds since the start of the recording (not the in-game clock).</summary>
    public float T { get; init; }

    /// <summary>PlayerState channel of the attacker, if the pawn could be assigned to a player.</summary>
    public ChannelIndex? AttackerChannel { get; init; }

    /// <summary>PlayerState channel of the victim.</summary>
    public ChannelIndex? VictimChannel { get; init; }

    public PlayerStats? Attacker { get; init; }
    public PlayerStats? Victim { get; init; }

    /// <summary>Damage of this hit.</summary>
    public float Amount { get; init; }

    public bool IsCritical { get; init; }
    public bool IsFatal { get; init; }
    public bool IsShield { get; init; }
    public bool IsShieldDestroyed { get; init; }
    public FVector? Location { get; init; }

    /// <summary>
    /// Class (archetype path) of the weapon that fired the hit, if known. A hit that arrives in the same frame as a weapon change
    /// is assigned to the previous weapon. Hits shortly after a change (see <see cref="SecondsSinceWeaponChange"/>) can still be uncertain.
    /// </summary>
    public string? Weapon { get; init; }

    /// <summary>Seconds between the attacker's last weapon change and this hit; -1 if no weapon was known.</summary>
    public float SecondsSinceWeaponChange { get; init; }

    /// <summary>The victim was knocked down (DBNO) when the hit arrived.</summary>
    public bool VictimWasDowned { get; init; }

    /// <summary>Attacker and victim are on the same team (different players).</summary>
    public bool IsTeamDamage { get; init; }

    /// <summary>Attacker and victim are the same player.</summary>
    public bool IsSelfDamage { get; init; }
}

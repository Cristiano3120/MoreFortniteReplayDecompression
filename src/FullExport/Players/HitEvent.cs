using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

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

    /// <summary><c>true</c>: <see cref="Weapon"/> comes from the cue itself (<c>SourceWeapon</c>, exact); <c>false</c>: guessed from the attacker's held weapon.</summary>
    public bool IsWeaponFromCue { get; init; }

    /// <summary>Seconds between the attacker's last weapon change and this hit; -1 if no weapon was known.</summary>
    public float SecondsSinceWeaponChange { get; init; }

    /// <summary>
    /// The victim was already knocked down (DBNO) before the hit arrived. The hit that knocks a player is not a hit on a downed player.
    /// Note: a knocked player is protected for about 2 seconds (see <see cref="IsDuringKnockProtection"/>).
    /// Hits in this time arrive with a magnitude of 0 (<see cref="Amount"/> is 0). Later hits on the downed player have normal damage.
    /// </summary>
    public bool VictimWasDowned => SecondsSinceVictimKnocked is not null;

    /// <summary>Seconds between the knock of the victim and this hit; <c>null</c> if the victim was not knocked (see <see cref="VictimWasDowned"/>).</summary>
    public float? SecondsSinceVictimKnocked { get; init; }

    /// <summary>
    /// The hit is on a downed player during the knock protection (about 2 seconds after the knock):
    /// <list type="bullet">
    /// <item>within <see cref="PlayerStats.KnockProtectionSureSeconds"/> after the knock: always protected</item>
    /// <item>up to <see cref="PlayerStats.KnockProtectionMaxSeconds"/> after the knock: only protected if the hit has 0 damage</item>
    /// <item>later: never protected</item>
    /// </list>
    /// </summary>
    public bool IsDuringKnockProtection => SecondsSinceVictimKnocked is float seconds &&
        (seconds < PlayerStats.KnockProtectionSureSeconds || (seconds < PlayerStats.KnockProtectionMaxSeconds && Amount == 0));

    /// <summary>Attacker and victim are on the same team (different players).</summary>
    public bool IsTeamDamage { get; init; }

    /// <summary>Attacker and victim are the same player.</summary>
    public bool IsSelfDamage { get; init; }
}

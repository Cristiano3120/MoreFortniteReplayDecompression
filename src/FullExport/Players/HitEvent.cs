using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Players;

/// <summary>
/// A single hit (attacker, target, damage, flags). Most hits come from the <c>BatchedDamageCues</c> RPC of the replay.
/// Hits of projectile weapons such as the slingshot are not part of it; they come from the gameplay cues on the victim
/// (shield damage cue plus the <c>Damage</c> attribute of the gameplay effect), merged into one hit.
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

    /// <summary>
    /// Damage of this hit as the replay reports it: the full damage of the weapon. For a hit that knocks or eliminates the victim
    /// this is more than the victim had left (a 90 damage hit on a victim with 20 health still has 90 here), see <see cref="Overkill"/>.
    /// </summary>
    public float Amount { get; init; }

    /// <summary>
    /// Part of <see cref="Amount"/> that went beyond the health and shield the victim had left, so it did not take anything away.
    /// 0 for a hit the victim survived. Example: 90 damage on a victim with 20 health left is 70 overkill.
    /// <para>
    /// <c>null</c> if the replay does not contain the health of the victim. A server replay has the health of every player,
    /// so the value is set for all hits. A normal (client) replay only has the health of the player who recorded it,
    /// so the value is only set for hits ON that player and <c>null</c> for all hits on enemies.
    /// Also <c>null</c> if <see cref="Export.ExportSettings.TrackHealth"/> is off.
    /// </para>
    /// <para>
    /// The value is calculated, not read: health and shield of the victim shortly before the hit, minus the other hits the
    /// victim took right before it. Healing in that moment or several hits in the same frame can shift it by a few points.
    /// </para>
    /// </summary>
    public float? Overkill { get; internal set; }

    /// <summary>
    /// <see cref="Amount"/> without <see cref="Overkill"/>: the health and shield the hit really took away.
    /// <c>null</c> if the overkill is unknown (see <see cref="Overkill"/>: only known in server replays, or for hits on the recording player).
    /// </summary>
    public float? AmountWithoutOverkill => Overkill is { } overkill ? Amount - overkill : null;

    public bool IsCritical { get; init; }
    /// <summary>
    /// The hit knocked or eliminated the victim. For hits of projectile weapons (reported as gameplay cues, without such a flag)
    /// this is derived from the knock or elimination of the victim at the same time.
    /// </summary>
    public bool IsFatal { get; internal set; }
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

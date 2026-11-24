using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Reading;

/// <summary>
/// Shots fired. The replay has no "shot" event; a shot shows up on the weapon actor:
/// <list type="bullet">
/// <item><c>AmmoCount</c> goes down by one per bullet, shell or projectile (also for the replay owner).</item>
/// <item><c>LastFireTimeVerified</c> changes once per shot (not replicated for the replay owner's own weapons in a client replay).</item>
/// <item>Shotguns and some pistols send a <c>BatchedDamageCues</c> with <c>bWeaponActivate</c> once per shot, also on a miss.</item>
/// </list>
/// Each signal alone can miss a shot (the ammo goes down and up again between two updates when the player reloads right after
/// a shot), so per weapon actor the signal with the most shots counts.
/// Most current weapons have no export group model in the library, so these fields are read directly from the properties of
/// every group that has an <c>AmmoCount</c>, and assigned to the player through the weapon's <c>Instigator</c> (its pawn).
/// </summary>
public sealed partial class FullExportReader
{
    // The names of a group's fields arrive bit by bit during the replay. Both caches are only cleared when one of the three
    // field names below arrives (rare), not on every new field name: clearing them on every new name cost ~20 % read time.

    /// <summary>Handles of the weapon fields (shots and item), per export group that has an <c>AmmoCount</c> or a <c>WeaponData</c>.</summary>
    private readonly Dictionary<NetFieldExportGroup, WeaponFields> _weaponFields = new();

    /// <summary>Groups that are known to have neither an <c>AmmoCount</c> nor a <c>WeaponData</c>.</summary>
    private readonly HashSet<NetFieldExportGroup> _noWeaponFields = new();

    /// <summary>Weapon actor GUID -> NetGUID of its item definition (<c>WeaponData</c>), resolved in <see cref="Cleanup"/>.</summary>
    private readonly Dictionary<ChannelGuid, uint> _weaponItemGuid = new();

    /// <summary>Weapon actor GUID -> item id (<c>WID_Shotgun_Break_Flintlock_Athena_SR</c>).</summary>
    private readonly Dictionary<ChannelGuid, string> _weaponItem = new();

    /// <summary>Everything read from one weapon actor, keyed by its actor GUID.</summary>
    private readonly Dictionary<ChannelGuid, WeaponFire> _weaponFire = new();

    /// <summary>
    /// Names of weapon classes that fire shots, for weapons that did not hit a player in a replay. Items like sprays, brooms,
    /// grapple gloves or the zipline deployer also use <c>AmmoCount</c> (some even send a fire time) but are no weapons;
    /// they match none of these.
    /// </summary>
    private static readonly Regex WeaponClassName = new(
        @"^B_(Assault|Shotgun|Pistol|SMG|Sniper|Launcher|DMR|Minigun|LMG|Rifle|Bow|Crossbow|Revolver)_|Slingshot|Dualies|_DMR_|Sniper|Launcher|Crossbow",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record WeaponFields(uint? AmmoCount, uint? LastFireTime, uint? Instigator, uint? WeaponData);

    private sealed class WeaponFire
    {
        public ChannelGuid? Instigator;
        public int? Ammo;

        /// <summary>One entry per ammo used (a drop by 3 between two updates adds three entries with the same time).</summary>
        public readonly List<float> AmmoShots = [];

        /// <summary>One entry per change of <c>LastFireTimeVerified</c>.</summary>
        public readonly List<float> FireTimes = [];

        /// <summary>One entry per <c>BatchedDamageCues</c> of this weapon with <c>bWeaponActivate</c> (sent once per shot by some weapons).</summary>
        public readonly List<float> ActivateCues = [];
    }

    public override bool ReceiveProperties(FBitArchive archive, NetFieldExportGroup group, uint channelIndex,
        [NotNullWhen(true)] out INetFieldExportGroup? exportGroup, bool enablePropertyChecksum = true, bool netDeltaUpdate = false)
    {
        if ((_settings.TrackShots || _settings.TrackWeaponItems) && !netDeltaUpdate && TryGetWeaponFields(group, out WeaponFields? fields) &&
            _channelGuid.TryGetValue(new ChannelIndex(channelIndex), out ChannelGuid weapon))
        {
            ReadWeaponFields(archive, fields, weapon, enablePropertyChecksum);
        }

        return base.ReceiveProperties(archive, group, channelIndex, out exportGroup, enablePropertyChecksum, netDeltaUpdate);
    }

    private bool TryGetWeaponFields(NetFieldExportGroup group, [NotNullWhen(true)] out WeaponFields? fields)
    {
        if (_weaponFields.TryGetValue(group, out fields))
        {
            return true;
        }

        if (_noWeaponFields.Contains(group))
        {
            return false;
        }

        uint? ammo = null, fireTime = null, instigator = null, weaponData = null;
        for (uint handle = 0; handle < group.NetFieldExportsLength; handle++)
        {
            switch (group.NetFieldExports[handle]?.Name)
            {
                case "AmmoCount": ammo = handle; break;
                case "LastFireTimeVerified": fireTime = handle; break;
                case "Instigator": instigator = handle; break;
                case "WeaponData": weaponData = handle; break;
            }
        }

        // Only the fields of switched on features count, so groups without them are skipped right away next time
        if (!_settings.TrackShots)
        {
            ammo = fireTime = null;
        }

        if (!_settings.TrackWeaponItems)
        {
            weaponData = null;
        }

        if (ammo is null && weaponData is null)
        {
            _noWeaponFields.Add(group);
            return false;
        }

        fields = new WeaponFields(ammo, fireTime, instigator, weaponData);
        _weaponFields[group] = fields;
        return true;
    }

    public override NetFieldExport? ReadNetFieldExport(FArchive archive)
    {
        NetFieldExport? export = base.ReadNetFieldExport(archive);
        if (export?.Name is "AmmoCount" or "LastFireTimeVerified" or "Instigator" or "WeaponData")
        {
            _weaponFields.Clear();
            _noWeaponFields.Clear();
        }

        return export;
    }

    /// <summary>Reads the shot and item fields of one property update without changing the archive position.</summary>
    private void ReadWeaponFields(FBitArchive archive, WeaponFields fields, ChannelGuid weapon, bool enablePropertyChecksum)
    {
        int start = archive.Position;
        try
        {
            if (enablePropertyChecksum)
            {
                archive.ReadBit();
            }

            WeaponFire? fire = null;
            while (!archive.AtEnd())
            {
                uint handle = archive.ReadIntPacked();
                if (handle == 0)
                {
                    break;
                }

                handle--;
                uint numBits = archive.ReadIntPacked();
                int end = archive.Position + (int)numBits;

                if (handle == fields.AmmoCount && numBits == 32)
                {
                    fire ??= WeaponFireOf(weapon);
                    int ammo = archive.ReadInt32();
                    if (fire.Ammo is { } previous && ammo < previous)
                    {
                        for (int i = 0; i < previous - ammo; i++)
                        {
                            fire.AmmoShots.Add(CurrentTimeSeconds);
                        }
                    }

                    fire.Ammo = ammo;
                }
                else if (handle == fields.LastFireTime && numBits == 32)
                {
                    fire ??= WeaponFireOf(weapon);
                    fire.FireTimes.Add(CurrentTimeSeconds);
                }
                else if (handle == fields.WeaponData && numBits > 0)
                {
                    uint item = archive.ReadIntPacked();
                    if (item != 0)
                    {
                        _weaponItemGuid[weapon] = item;
                    }
                }
                else if (handle == fields.Instigator && numBits > 0 && fields.AmmoCount is not null)
                {
                    uint instigator = archive.ReadIntPacked();
                    if (instigator != 0)
                    {
                        (fire ??= WeaponFireOf(weapon)).Instigator = new ChannelGuid(instigator);
                    }
                }

                archive.Seek(end);
            }
        }
        finally
        {
            archive.Seek(start);
        }
    }

    private WeaponFire WeaponFireOf(ChannelGuid weapon)
    {
        if (!_weaponFire.TryGetValue(weapon, out WeaponFire? fire))
        {
            fire = new WeaponFire();
            _weaponFire[weapon] = fire;
        }

        return fire;
    }

    /// <summary>
    /// Gives every player the shots of the weapons their pawn fired. Per weapon actor the signal with the most shots counts:
    /// ammo drops, fire time changes or activation cues.
    /// </summary>
    private void AssignShots()
    {
        foreach (PlayerStats stats in _stats.Values)
        {
            stats.RawShots.Clear();
        }

        // A class is a weapon if it hit a player in this replay or its name says so. A fire time alone is not enough:
        // tools like the zipline deployer or the mounted cannon also send one (the zipline deployer made Bergii's shots
        // 5 too many compared with Epic's stats).
        HashSet<string> hitPlayers = [];
        foreach (PendingHit hit in _pendingHits)
        {
            if (hit.Victim is not null && WeaponClass(hit.WeaponGuid) is { } weaponClass)
            {
                hitPlayers.Add(weaponClass);
            }
        }

        foreach ((ChannelGuid weapon, WeaponFire fire) in _weaponFire)
        {
            if (WeaponClass(weapon) is not { } weaponClass || !(hitPlayers.Contains(weaponClass) || WeaponClassName.IsMatch(ShortClass(weaponClass))))
            {
                continue;
            }

            if (PlayerOfPawn(fire.Instigator) is not { } player)
            {
                continue;
            }

            List<float> shots = new[] { fire.AmmoShots, fire.FireTimes, fire.ActivateCues }.MaxBy(list => list.Count)!;
            foreach (float t in shots)
            {
                player.RawShots.Add((t, weaponClass, WeaponItem(weapon)));
            }
        }
    }

    /// <summary>Item id of a weapon actor (<c>WID_Shotgun_Break_Flintlock_Athena_SR</c>), <c>null</c> if unknown.</summary>
    private string? WeaponItem(ChannelGuid? weaponGuid)
        => weaponGuid is { } guid ? _weaponItem.GetValueOrDefault(guid) : null;

    /// <summary>
    /// The library clears its NetGUID to path table at the end of the reading, before the players are built,
    /// so the item ids are resolved right before that.
    /// </summary>
    protected override void Cleanup()
    {
        foreach ((ChannelGuid weapon, uint item) in _weaponItemGuid)
        {
            if (_netGuidCache.TryGetPathName(item, out string? path))
            {
                _weaponItem[weapon] = ShortClass(path);
            }
        }

        base.Cleanup();
    }

    /// <summary>Last part of an archetype path: <c>/Game/.../B_Assault_Flintlock.B_Assault_Flintlock_C</c> -> <c>B_Assault_Flintlock_C</c>.</summary>
    private static string ShortClass(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOf('/');
        return path[(Math.Max(dot, slash) + 1)..];
    }
}

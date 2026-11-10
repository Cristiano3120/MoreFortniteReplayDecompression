using System.Diagnostics;

namespace MoreFortniteReplayDecompression;

internal class Program
{
    public static void Main()
    {
        //Guck ob du perf improven kannst
        //ersetz of durch propertys (=> ) die of callen z.B HealthUpdates => .Of<HealthSet> mach das für jede class
        //für die es möglich ist oder guck ist glaube schon in PlayerStast
        //außer für PlayerStats lass das so wie das gerade ist als property
        //Inventar: seit 43.00 läuft das über /Script/ItemizationCoreRuntime.InventoryReplicator (noch kein Model), FortInventory kommt nicht mehr
        //Builds: die Build-Models in FortniteReplayReader\Models\NetFieldExports\Builds sind pro Pfad hart codiert und auf ParseMode.Debug
        //Schreib test: Convert ein replay in Json und gleich dann immer mit neuer version ab?

        int matchCount = 0;
        float msUsedComputing = 0;
        float artixDmg = 0;
        float artixKnockDmg = 0;
        float aliDmg = 0;
        float aliKnockDmg = 0;
        float artixDmgTaken = 0;
        float aliDmgTaken = 0;

        float bergiiDmg = 0;
        float bergiiKnockDmg = 0;
        float bergiiDmgTaken = 0;
        int bergiiHits = 0;
        int bergiiDownedHits = 0;
        int bergiiHeadshots = 0;
        int bergiiStructureHits = 0;
        uint bergiiKills = 0;
        float bergiiHealthHealed = 0;
        float bergiiShieldHealed = 0;
        float bergiiTimeAlive = 0;
        float bergiiTimeInStorm = 0;
        float bergiiStormDamage = 0;
        float bergiiDistance = 0;

        // Epic's own end-of-match stats of the replay owner (ground truth for the player who recorded the replay)
        uint epicEliminations = 0;
        uint epicWeaponDamage = 0;
        uint epicDamageToPlayers = 0;
        uint epicDamageTaken = 0;
        uint epicTraveled = 0;

        int artixHits = 0;
        int aliHits = 0;
        int artixHitsWithKnocks = 0;
        int artixHitsWithPre = 0;
        int artixAllHits = 0;
        int artixStructureHits = 0;

        foreach (string filepath in Directory.EnumerateFiles("C:\\Users\\Cristiano\\Desktop\\FCS\\MatchReplay\\BergiiReplays\\BergiiSkinCup"))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Match: " + ++matchCount);
            
            ReplayExport export = ReplayExporter.Read(filepath, ExportSettings.StatsOnly);
            PlayerStats bergii = export.Players.Where(x => x.Name == "Detect Bergii yh").First();
  
            stopwatch.Stop();
            msUsedComputing += stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"Battle Bus start t: {export.MatchStartTime ?? -1}\n");
            Console.WriteLine($"Kills: {bergii.Kills}, Knocks: {bergii.Knocks}, Placement: {bergii.Placement}\n");
            Console.WriteLine($"Bergii ID: {bergii.EpicId}");
            Console.WriteLine($"Bergii pre round dmg: {bergii.PreMatchDamageDealt}");
            Console.WriteLine($"Bergii round dmg: {bergii.DamageDealt}");
            Console.WriteLine($"Bergii knocked dmg: {bergii.DamageDealtToDowned} ({bergii.DownedHits} hits)\n");
            Console.WriteLine("FinalHealth: " + "H: " + (bergii.FinalHealth ?? 0) + ", " + "S: " + (bergii.FinalShield ?? 0));
            Console.WriteLine($"Healed: H: {bergii.HealthHealed}, S: {bergii.ShieldHealed}");
            Console.WriteLine($"Storm: {bergii.TimeInStorm:F0}s, {bergii.StormDamageTaken:F0} dmg");
            Console.WriteLine($"Supply drops: {string.Join(", ", bergii.SupplyDropDistances.Select(d => $"{d.Meters:F0}m"))}");

            if (bergii.EpicMatchStats is { } epic)
            {
                Console.WriteLine($"Epic: elims {epic.Eliminations}, dmg {epic.WeaponDamage} + {epic.OtherDamage} other, taken {epic.DamageTaken}");

                epicEliminations += epic.Eliminations;
                epicWeaponDamage += epic.WeaponDamage;
                epicDamageToPlayers += epic.DamageToPlayers;
                epicDamageTaken += epic.DamageTaken;
                epicTraveled += epic.TotalTraveled;
            }

            bergiiDmg += bergii.DamageDealt;
            bergiiKnockDmg += bergii.DamageDealtToDowned;
            bergiiDmgTaken += bergii.DamageTaken;
            bergiiHits += bergii.Hits;
            bergiiDownedHits += bergii.DownedHits;
            bergiiHeadshots += bergii.CriticalHits;
            bergiiStructureHits += bergii.StructureHits;
            bergiiKills += bergii.Kills ?? 0;
            bergiiHealthHealed += bergii.HealthHealed;
            bergiiShieldHealed += bergii.ShieldHealed;
            bergiiTimeAlive += bergii.TimeAlive;
            bergiiTimeInStorm += bergii.TimeInStorm;
            bergiiStormDamage += bergii.StormDamageTaken;
            bergiiDistance += bergii.DistanceTraveled;

            Console.WriteLine("Time: " + stopwatch.ElapsedMilliseconds + "ms\n");
        }

        Console.WriteLine("Final Stats for Bergii (ours | Epic):\n");
        Console.WriteLine($"Bergii Kills: {bergiiKills} | {epicEliminations}");
        Console.WriteLine($"Bergii Dmg: {bergiiDmg} | {epicWeaponDamage} weapon, {epicDamageToPlayers} total");
        Console.WriteLine($"Bergii Dmg without knocked: {bergiiDmg - bergiiKnockDmg}");
        Console.WriteLine($"Bergii Dmg Taken: {bergiiDmgTaken} | {epicDamageTaken} (Epic includes storm and fall damage)");
        Console.WriteLine($"Bergii Hits: {bergiiHits} ({bergiiDownedHits} on downed players)");
        Console.WriteLine($"Bergii Headshots: {bergiiHeadshots}");
        Console.WriteLine($"Bergii StructureHits: {bergiiStructureHits}");
        Console.WriteLine($"Bergii Healed: H: {bergiiHealthHealed}, S: {bergiiShieldHealed}");
        Console.WriteLine($"Bergii Time Alive: {TimeSpan.FromSeconds(bergiiTimeAlive):hh\\:mm\\:ss}");
        Console.WriteLine($"Bergii Storm: {bergiiTimeInStorm:F0}s, {bergiiStormDamage:F0} dmg");
        Console.WriteLine($"Bergii Distance: {bergiiDistance:F0}m | {epicTraveled / 100}m");
        Console.WriteLine();
        Console.WriteLine("Time over " + matchCount + " matches: " + msUsedComputing / 1000 + "s");
        
        matchCount = 0;
        msUsedComputing = 0;
        foreach (string filepath in Directory.EnumerateFiles("C:\\Users\\Cristiano\\Desktop\\FCS\\MatchReplay\\ArtixReplays"))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Match: " + ++matchCount);

            ReplayExport export = ReplayExporter.Read(filepath, collectTimeline: false);

            PlayerStats artix = export.Players.Where(x => x.Name == "artix 10ǃ").First();
            PlayerStats ali = export.Players.Where(x => x.Name == "Ailton 9ǃ").First();

            stopwatch.Stop();
            msUsedComputing += stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"Battle Bus start t: {export.Players.First().MatchStartTime ?? -1}\n");

            Console.WriteLine($"Teamkills: {artix.TeamKills}\n");
            Console.WriteLine($"Artix ID: {artix.EpicId}");
            Console.WriteLine($"Artix pre round dmg: {artix.PreMatchDamageDealt}");
            Console.WriteLine($"Artix round dmg: {artix.DamageDealt}");
            Console.WriteLine($"Artix knocked dmg: {artix.DamageDealtToDowned}\n");
            Console.WriteLine("FinalHealth: " + "H: " + (artix.FinalHealth ?? 0) + ", " + "S: " + (artix.FinalShield ?? 0));
            artixDmg += artix.DamageDealt;
            artixKnockDmg += artix.DamageDealtToDowned;
            artixDmgTaken += artix.DamageTaken;
            artixHits += artix.Hits - artix.PreMatchHits - artix.DownedHits;
            artixAllHits += artix.Hits;
            artixHitsWithPre += artix.Hits - artix.DownedHits;
            artixHitsWithKnocks += artix.Hits - artix.PreMatchHits;
            artixStructureHits += artix.StructureHits;
        
            Console.WriteLine($"Ali pre round dmg: {ali.PreMatchDamageDealt}");
            Console.WriteLine($"Ali round dmg: {ali.DamageDealt}");
            Console.WriteLine($"Ali knocked dmg: {ali.DamageDealtToDowned}\n");
            aliDmg += ali.DamageDealt;
            aliKnockDmg += ali.DamageDealtToDowned;
            aliDmgTaken += ali.DamageTaken;
            aliHits += ali.Hits - ali.PreMatchHits - ali.DownedHits;

            Console.WriteLine("Time: " + stopwatch.ElapsedMilliseconds + "ms\n");
        }

        Console.WriteLine("Artix Dmg:" + artixDmg);
        Console.WriteLine($"Artix Dmg without knocked: {artixDmg - artixKnockDmg}");
        Console.WriteLine($"Artix Dmg Taken: {artixDmgTaken}");
        Console.WriteLine($"Artix Hits: {artixHits}");
        Console.WriteLine($"Artix WithPreHits: {artixHitsWithPre}");
        Console.WriteLine($"Artix WithKnockHits: {artixHitsWithKnocks}");
        Console.WriteLine($"Artix AllHits: {artixHits}");
        Console.WriteLine($"Artix StructureHits: {artixStructureHits}");
        Console.WriteLine();

        Console.WriteLine("ali Dmg:" + aliDmg);
        Console.WriteLine($"ali Dmg without knocked: {aliDmg - aliKnockDmg}");
        Console.WriteLine($"Ali Dmg Taken: {aliDmgTaken}");
        Console.WriteLine($"Ali Hits: {aliHits}");
        Console.WriteLine();

        Console.WriteLine("Time over " + matchCount + " matches: " + msUsedComputing / 1000 + "s");
    }
}

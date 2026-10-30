using System.Diagnostics;
using FortniteReplayReader;
using FortniteReplayReader.Models;

namespace MoreFortniteReplayDecompression;

internal class Program
{
    public static void Main()
    {
        //Mach settings um z.B health etc rauszufiltern ?= mehr performance viele models in FortniteReplayReader\Models
        //sind auf parseType ignore mach das dynamisch via settings
        //Guck ob du perf improven kannst
        //ersetz of durch propertys (=> ) die of callen z.B HealthUpdates => .Of<HealthSet> mach das für jede class
        //für die es möglich ist oder guck ist glaube schon in PlayerStast
        //außer für PlayerStats lass das so wie das gerade ist als property
        //Entfern die ganzen summen sachen also die die für die ganzen lobbys gelten
        //Guck wegen inventar also das jede PlayerStats instanz auch ne List von Inventory instanzen hat oder so  uns zones das du das anzeigen kannst
        //Mach für jede class ne file maybe sogar partial files wie PlayerStats.health etc

        int matchCount = 0;
        float msUsedComputing = 0;
        float artixDmg = 0;
        float artixKnockDmg = 0;
        float aliDmg = 0;
        float aliKnockDmg = 0;
        float artixDmgTaken = 0;
        float aliDmgTaken = 0;
        int artixHits = 0;
        int aliHits = 0;


        foreach (string filepath in Directory.EnumerateFiles("C:\\Users\\Cristiano\\Desktop\\FCS\\MatchReplay\\ArtixReplays"))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Match: " + ++matchCount);

            ReplayExport export = ReplayExporter.Read(filepath, collectTimeline: false);
            PlayerStats artix = export.Players.Where(x => x.Name == "artix 10ǃ").First();
            PlayerStats ali = export.Players.Where(x => x.Name == "Ailton 9ǃ").First();

            Console.WriteLine($"Battle Bus start t: {export.Players.First().MatchStartTime ?? -1}\n");

            Console.WriteLine($"Teamkills: {artix.TeamKills}\n");
            Console.WriteLine($"Artix ID: {artix.EpicId}");
            Console.WriteLine($"Artix pre round dmg: {artix.PreMatchDamageDealt}");
            Console.WriteLine($"Artix round dmg: {artix.DamageDealt}");
            Console.WriteLine($"Artix knocked dmg: {artix.DamageDealtToDowned}\n");
            artixDmg += artix.DamageDealt;
            artixKnockDmg += artix.DamageDealtToDowned;
            artixDmgTaken += artix.DamageTaken;
            artixHits += artix.Hits - artix.PreMatchHits;
        
            Console.WriteLine($"Ali pre round dmg: {ali.PreMatchDamageDealt}");
            Console.WriteLine($"Ali round dmg: {ali.DamageDealt}");
            Console.WriteLine($"Ali knocked dmg: {ali.DamageDealtToDowned}\n");
            aliDmg += ali.DamageDealt;
            aliKnockDmg += ali.DamageDealtToDowned;
            aliDmgTaken += ali.DamageTaken;
            aliHits += ali.Hits - ali.PreMatchHits;

            stopwatch.Stop();
            msUsedComputing += stopwatch.ElapsedMilliseconds;
            Console.WriteLine("Time: " + stopwatch.ElapsedMilliseconds + "ms\n");
        }

        Console.WriteLine("Artix Dmg:" + artixDmg);
        Console.WriteLine($"Artix Dmg without knocked: {artixDmg - artixKnockDmg}");
        Console.WriteLine($"Artix Dmg Taken: {artixDmgTaken}");
        Console.WriteLine($"Artix Hits: {artixHits}");
        Console.WriteLine();

        Console.WriteLine("ali Dmg:" + aliDmg);
        Console.WriteLine($"ali Dmg without knocked: {aliDmg - aliKnockDmg}");
        Console.WriteLine($"Ali Dmg Taken: {aliDmgTaken}");
        Console.WriteLine($"Ali Hits: {aliHits}");
        Console.WriteLine();

        Console.WriteLine("Time over " + matchCount + " matches: " + msUsedComputing / 1000 + "s");
    }
}

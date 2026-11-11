# MoreFortniteReplayDecompression

C# parser for your Fortnite replays, with **detailed stats for every player**.

This is a fork of [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor).
The original library reads the replay file. This fork adds a layer on top of it (`FullExport`) that turns the raw replay data
into easy to use stats per player, and fixes a few bugs in the library itself.

## Features

- **Damage and hits:** damage dealt and taken, hits, headshots, shield hits, damage to knocked players, damage to builds, every single hit with weapon and location
- **Eliminations:** knocks, eliminations, assists, deaths, time alive
- **Health:** health and shield over time, final health, health and shield healed
- **Building:** every piece a player built, materials used per type (wood, stone, metal)
- **Movement:** positions over time, distance traveled, time in the storm, storm damage
- **Match:** storm phases, supply drops and how far each player was away from them
- **Settings:** turn off the data you don't need to read replays faster

...and a lot more: more player stats, weapons used, teammates, the full timeline of every update and more.

## Getting Started

.NET 10.0 is required.

> A NuGet package is coming soon. Until then, clone this repository and add a reference to the `FullExport` project:

```powershell
git clone https://github.com/Cristiano3120/MoreFortniteReplayDecompression.git
dotnet add reference MoreFortniteReplayDecompression/src/FullExport/MoreFortniteReplayDecompression.csproj
```

Read a replay:

```csharp
using MoreFortniteReplayDecompression.Export;
using MoreFortniteReplayDecompression.Players;

ReplayExport export = ReplayExporter.Read("your-amazing-fortnite.replay", ExportSettings.StatsOnly);

foreach (PlayerStats player in export.Players)
{
    Console.WriteLine($"#{player.Placement} {player.Name}: {player.Eliminations} elims, {player.DamageDealt:F0} damage");
}
```

## Examples

### Stats of one player

```csharp
PlayerStats? player = export.Player("Some Player");

Console.WriteLine($"Eliminations: {player.Eliminations}");
Console.WriteLine($"Assists:      {player.Assists}");
Console.WriteLine($"Knocks:       {player.Knocks}");
Console.WriteLine($"Damage dealt: {player.DamageDealt:F0}");
Console.WriteLine($"Damage taken: {player.DamageTaken:F0}");
Console.WriteLine($"Headshots:    {player.CriticalHits}");
Console.WriteLine($"Time alive:   {TimeSpan.FromSeconds(player.TimeAlive).ToString(@"mm\:ss")}");
Console.WriteLine($"Distance:     {player.DistanceTraveled:F0} m");
```

### Building and materials

```csharp
Materials used = player.MaterialsUsed;
Console.WriteLine($"Wood: {used.Wood}, Stone: {used.Stone}, Metal: {used.Metal}, Total: {used.Total}");

// Materials used until a point in time (replay time in seconds)
Materials firstFiveMinutes = player.MaterialsUsedUntil((export.MatchStartTime ?? 0) + 300);

foreach (BuildEvent build in player.Builds)
{
    Console.WriteLine($"{build.T:F1}s: {build.Material} {build.Piece}");
}
```

### Every hit

```csharp
foreach (HitEvent hit in player.DamageDealtEvents)
{
    Console.WriteLine($"{hit.T:F1}s: {hit.Amount:F0} damage to {hit.Victim?.Name} with {hit.Weapon}");
}
```

### Health at the end and over time

```csharp
Console.WriteLine($"Final health: {player.FinalHealth}, final shield: {player.FinalShield}");

foreach (HealthSample sample in player.HealthTimeline)
{
    Console.WriteLine($"{sample.T:F1}s: {sample.Health} hp, {sample.Shield} shield");
}
```

### Questions about a point in time

```csharp
float t = 600; // replay time in seconds

FVector? position = player.PositionAt(t); // using Unreal.Core.Models;
bool knocked = player.IsDowned(t);
```

### Read faster

Turn off what you don't need. Switched off data is not parsed at all.

```csharp
ExportSettings settings = new()
{
    CollectTimeline = false, // no full timeline of every update (biggest saving)
    TrackHealth = false,     // no health / shield
    TrackBuilds = false,     // no builds and materials
};

ReplayExport export = ReplayExporter.Read("your-amazing-fortnite.replay", settings);
```

`ExportSettings.StatsOnly` keeps all stats but skips the timeline. `ExportSettings.Full` reads everything.

## What the replay does not contain

Some things are simply not in the replay file, so this fork can't give them to you:

- **Inventory:** the items (and the materials a player has right now) are not recorded.
- **Farming:** how many materials a player gathered is not recorded.
- **Players far away (client replays only):** a replay recorded by a player only has data about players that were close to them,
  so the stats of that player and their team are the most complete. Server replays contain every player.

## Changes to the library

Bug fixes and additions to the original library (also sent as pull requests to the original repository):

- Health and shield are read correctly
- Export groups that are sent late are not lost any more
- The player who recorded the replay is always found
- Export groups can be skipped to read faster
- One class can read all export groups with the same path prefix (used for all build pieces)

## Original project

The replay parsing itself is the work of the original project. See its
[documentation](https://fortnitereplaydecompressor.readthedocs.io/en/latest/) and [Discord server](https://discord.gg/p5CMqJC).

Special thanks from the original project to [Kuinox](https://github.com/Kuinox/ChartsNite), [ApertureC](https://github.com/ApertureC/),
[AlpaGit](https://github.com/AlpaGit) and [SL-x-TnT](https://github.com/SL-x-TnT).

## License
Licensed under the [MIT License](LICENSE).

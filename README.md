[![chat](https://img.shields.io/badge/chat-on%20discord-7289DA?logo=discord&logoColor=white)](https://discord.gg/TTZV6a8F3F)
 # MoreFortniteReplayDecompression

C# parser for your Fortnite replays, with **detailed stats for every player**.

This is a fork of [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor).
The original library reads the replay file. This fork adds a layer on top of it (`FullExport`) that turns the raw replay data
into easy to use stats per player.

## Features

- **Damage and hits:** damage dealt and taken, hits, headshots, shield hits, damage to knocked players, damage to builds, every single hit with weapon and location
- **Damage details:** overkill, projectile damage (slingshot, launchers), damage during knock protection, team and self damage, damage before the battle bus
- **Fights:** every fight against an opponent with duration, damage and outcome, time to knock and time to kill
- **Shots and accuracy:** shots fired per weapon (also slingshots and launchers), hits per weapon, accuracy
- **Eliminations:** knocks, eliminations, assists, deaths, time alive, every elimination with time and players
- **Health:** health and shield over time, final health, health and shield healed, knocked intervals
- **Building:** every piece a player built, materials used per type (wood, stone, metal)
- **Movement:** positions over time, distance traveled, time in the storm, storm damage, optional speed, rotation and states (crouched, sprinting, skydiving, ...)
- **Weapons and team:** every weapon a player held, teammates, bots
- **Epic's own stats:** the end-of-match stats Epic shows the player who recorded the replay
- **Match:** storm phases, supply drops and how far each player was away from them
- **Metadata:** match start, length, tournament and playlist in a few milliseconds without reading the whole replay
- **Timeline:** every update of the replay as typed objects, if you need more than the stats
- **Many replays:** read replays in parallel, with or without keeping them in memory
- **Settings:** turn on only the data you need to read replays faster

## Getting Started

.NET 10.0 or .NET 11.0 is required.

Install the [NuGet package](https://www.nuget.org/packages/MoreFortniteReplayDecompression):

```powershell
dotnet add package MoreFortniteReplayDecompression
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

`ExportSettings.StatsOnly` reads all player stats. See [Settings](#settings) for reading only what you need.

All times in the stats are **replay time in seconds** (`T`, `Start`, `DeathTime`, ...), counted from the start of the recording.
`export.MatchStartTime` is the replay time of the battle bus. Stats like damage, hits and eliminations only count from there on;
everything before it (the starter island) is in the `PreMatch...` values.

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

The player who recorded the replay is `export.ReplayOwner`. Bots are not in `export.Players` but in `export.Bots`.

### Shots and accuracy

```csharp
Console.WriteLine($"Shots fired: {player.ShotsFired}, accuracy {player.Accuracy:P1}");

foreach (WeaponShots weapon in player.WeaponShots)
{
    Console.WriteLine($"{weapon.Weapon}: {weapon.ShotsFired} shots, {weapon.Hits} hits ({weapon.Accuracy:P1})");
}
```

Every weapon also has its exact item with rarity (`WID_Shotgun_Break_Flintlock_Athena_SR` = legendary), so you can split by rarity:

```csharp
foreach (WeaponShots weapon in player.WeaponShots)
{
    foreach (WeaponItemShots item in weapon.Items)
    {
        Console.WriteLine($"{item.Item} ({item.Rarity}): {item.ShotsFired} shots, {item.Hits} hits ({item.Accuracy:P1})");
    }
}

// Damage per item, from the single hits
foreach (var group in player.DamageDealtEvents.GroupBy(h => h.WeaponItem))
{
    Console.WriteLine($"{group.Key}: {group.Sum(h => h.Amount):F0} damage");
}
```

The replay has no display names like "Pump Shotgun", only these ids. `ItemIds.BaseId(item)` removes the rarity
(`WID_Shotgun_Break_Flintlock_Athena`), which makes it a good key for your own table of display names.

One shot is one bullet, shell or projectile: a shotgun shot counts once. Epic's own accuracy (`player.EpicAccuracy`, only for the
player who recorded the replay) counts every shotgun pellet instead, so the two values differ when shotguns were used.

### Fights and time to kill

```csharp
Console.WriteLine($"Fights won: {player.FightsWon}, lost: {player.FightsLost}");
Console.WriteLine($"Time to knock: {player.AverageTimeToKnock:F1} s (fastest {player.FastestTimeToKnock:F1} s)");
Console.WriteLine($"Time to kill:  {player.AverageTimeToKill:F1} s (fastest {player.FastestTimeToKill:F1} s)");

foreach (FightEvent fight in player.Fights)
{
    Console.WriteLine($"{fight.Start:F0}s vs {fight.Opponent.Name}: {fight.Duration:F1} s, {fight.Outcome}, time to knock {fight.TimeToKnock:F1} s");
    Console.WriteLine($"    dealt {fight.DamageDealt:F0} in {fight.HitsDealt} hits, took {fight.DamageTaken:F0} in {fight.HitsTaken} hits");
}
```

The replay has no fights, so they are built from the hits between two players. A fight ends when one of them is eliminated,
when they were more than 100 m apart for over 30 s, or after 3 minutes without a hit. From zone 5 on everybody is close
together, so there a fight already ends after 30 s without a hit (`fight.IsEndgame`). Time to knock and time to kill are
counted from the player's own first hit in the fight.

### Eliminations, assists and deaths

```csharp
foreach (EliminationEvent elimination in player.EliminationEvents)
{
    string kind = elimination.IsKnock ? "knocked" : "eliminated";
    Console.WriteLine($"{elimination.T:F0}s: {kind} {elimination.Eliminated?.Name}");
}

foreach (EliminationEvent assist in player.AssistEvents)
{
    Console.WriteLine($"{assist.T:F0}s: assist, {assist.Eliminated?.Name} eliminated by {assist.CreditedTo?.Name}");
}

if (player.IsEliminated)
{
    EliminationEvent death = player.DeathEvents[^1];
    Console.WriteLine($"Eliminated at {player.DeathTime:F0}s by {death.CreditedTo?.Name}, placement #{player.Placement}");
}
```

`Eliminator` is the player named in the kill feed, `CreditedTo` is the player who gets the elimination: when a knocked
player is finished by someone else, Fortnite gives it to the player who knocked them.

### Damage in detail

```csharp
Console.WriteLine($"Damage dealt:            {player.DamageDealt:F0}");
Console.WriteLine($"  without overkill:      {player.DamageDealtWithoutOverkill:F0}"); // damage beyond the victim's health and shield
Console.WriteLine($"  to knocked players:    {player.DamageDealtToDowned:F0}");
Console.WriteLine($"  without knocked:       {player.DamageDealtWithoutKnocked:F0}");
Console.WriteLine($"  with projectiles:      {player.ProjectileDamageDealt:F0} in {player.ProjectileHits} hits");
Console.WriteLine($"  to teammates and self: {player.DamageDealtToTeammates:F0} / {player.DamageDealtToSelf:F0}");
Console.WriteLine($"Damage to builds:        {player.DamageDealtToStructures:F0} in {player.StructureHits} hits");
Console.WriteLine($"Before the battle bus:   {player.PreMatchDamageDealt:F0}");
```

Damage amounts are the full weapon damage, also on the hit that knocks or eliminates (Epic counts it the same way).
Overkill needs the health of the victim, so it is only known in server replays and for hits on the player who recorded the replay
(`null` otherwise).

### Every hit

```csharp
foreach (HitEvent hit in player.DamageDealtEvents)
{
    string flags = (hit.IsCritical ? " head" : "") + (hit.IsShield ? " shield" : "") + (hit.IsFatal ? " fatal" : "");
    Console.WriteLine($"{hit.T:F1}s: {hit.Amount:F0} damage to {hit.Victim?.Name} with {hit.Weapon}{flags}");
}

// The 5 biggest hits the player took
foreach (HitEvent hit in player.DamageTakenEvents.OrderByDescending(h => h.Amount).Take(5))
{
    Console.WriteLine($"{hit.Amount:F0} from {hit.Attacker?.Name} with {hit.Weapon}");
}
```

### Health at the end and over time

```csharp
Console.WriteLine($"Final health: {player.FinalHealth}, final shield: {player.FinalShield}");
Console.WriteLine($"Healed: {player.HealthHealed:F0} health, {player.ShieldHealed:F0} shield");

foreach (HealthSample sample in player.HealthTimeline)
{
    Console.WriteLine($"{sample.T:F1}s: {sample.Health} hp, {sample.Shield} shield");
}

foreach (DownedInterval downed in player.DownedIntervals)
{
    Console.WriteLine($"Knocked from {downed.Start:F0}s to {downed.End:F0}s");
}
```

A client replay only contains the health of the player who recorded it. Server replays have the health of everyone.

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

### Positions, storm and supply drops

```csharp
using MoreFortniteReplayDecompression.Match;
using Unreal.Core.Models;

foreach (PositionSample sample in player.Positions)
{
    Console.WriteLine($"{sample.T:F0}s: {sample.Location}");
}

Console.WriteLine($"Time in storm: {player.TimeInStorm:F0} s, storm damage {player.StormDamageTaken:F0}");

foreach (StormPhase phase in export.StormPhases)
{
    Console.WriteLine($"Zone {phase.Index}: shrinks from {phase.StartT:F0}s to {phase.EndT:F0}s, radius {phase.FromRadius / 100:F0} m -> {phase.ToRadius / 100:F0} m");
}

// Safe zone at a point in time, and whether a location was in the storm then
float t = 900;
if (Storm.SafeZoneAt(export.StormPhases, t) is { } zone)
{
    Console.WriteLine($"Safe zone at {t}s: center {zone.Center}, radius {zone.Radius / 100:F0} m");
}

FVector? position = player.PositionAt(t);
bool inStorm = position is not null && Storm.IsInStorm(export.StormPhases, t, position);

foreach (SupplyDropDistance drop in player.SupplyDropDistances)
{
    Console.WriteLine($"Supply drop at {drop.Drop.SpawnT:F0}s was {drop.Meters:F0} m away");
}
```

Positions are in centimeters like in the game; distances like `DistanceTraveled` and `Meters` are in meters.
By default one position per second is stored (`ExportSettings.PositionInterval`, 0 = every update).

### Speed, rotation and states

Off by default because it stores a lot. Turn it on with `TrackMovementDetails`:

```csharp
ReplayExport export = ReplayExporter.Read("your-amazing-fortnite.replay", ExportSettings.StatsOnly with { TrackMovementDetails = true });

foreach (MovementSample sample in export.Player("Some Player")!.Movement)
{
    if (sample.Has(MovementFlags.Crouched) || sample.Has(MovementFlags.Skydiving))
    {
        Console.WriteLine($"{sample.T:F0}s: {sample.Speed:F1} m/s, flags {sample.Flags}");
    }
}
```

### Questions about a point in time

```csharp
float t = 600; // replay time in seconds

FVector? position = player.PositionAt(t); // using Unreal.Core.Models;
bool knocked = player.IsDowned(t);
Materials usedSoFar = player.MaterialsUsedUntil(t);
```

### Weapons and teammates

```csharp
foreach (WeaponChange change in player.EquippedWeapons)
{
    Console.WriteLine($"{change.T:F1}s: took out {change.Weapon}");
}

Console.WriteLine($"Team {player.TeamIndex}: {string.Join(", ", player.Teammates.Select(m => m.Name))}");

foreach (PlayerStats bot in export.Bots)
{
    Console.WriteLine($"Bot {bot.Name}: {bot.DamageTaken:F0} damage taken");
}
```

### Epic's own stats

Client replays contain the end-of-match stats Epic shows the player who recorded the replay:

```csharp
if (export.ReplayOwner?.EpicMatchStats is { } epic)
{
    Console.WriteLine($"Epic: {epic.Eliminations} elims, {epic.DamageToPlayers} damage, {epic.Accuracy:P1} accuracy");
    Console.WriteLine($"Materials gathered: {epic.MaterialsGathered}, used: {epic.MaterialsUsed}, traveled: {epic.TotalTraveled}");
}
```

### Only the metadata

When, how long, which playlist and whether it was a tournament. Only the start of the replay is read, so this takes a few
milliseconds instead of seconds (about 2 to 10 ms per replay when several replays are read, even for a 130 MB server replay; the very first read of a program takes longer).

```csharp
ReplayMetadata metadata = ReplayExporter.ReadMetadata("your-amazing-fortnite.replay");

Console.WriteLine($"Match started: {metadata.MatchStartedUtc} UTC, length {metadata.Length}");
Console.WriteLine($"Tournament:    {metadata.TournamentCodeName}"); // e.g. tournament_epicgames_s43_onepiecezorocup_eu
Console.WriteLine($"Playlist:      {metadata.Playlist}");
Console.WriteLine($"Server replay: {metadata.IsServerReplay}, version {metadata.FortniteVersion}");
```

The same values are in `export.Metadata` after a normal read. `ExportSettings.Metadata` does the same through `ReplayExporter.Read`.

Use it to pick replays before reading them fully:

```csharp
List<string> tournamentReplays = Directory.EnumerateFiles("replays", "*.replay")
    .Where(path => ReplayExporter.ReadMetadata(path).IsTournament)
    .ToList();
```

### Settings

Every switch is off by default, so you only pay for what you turn on. Switched off data is not parsed at all.

```csharp
ExportSettings settings = new()
{
    TrackHits = true,      // damage and hits
    TrackPositions = true, // positions, storm and distance
};

ReplayExport export = ReplayExporter.Read("your-amazing-fortnite.replay", settings);
```

`ExportSettings.StatsOnly` turns on all stats but no timeline. `ExportSettings.Full` also collects the full timeline
(`CollectTimeline`), which is the biggest cost. Start from one of them and change single switches with `with`:

```csharp
ExportSettings settings = ExportSettings.StatsOnly with { TrackBuilds = false };
```

| Setting | What it reads |
|---|---|
| `TrackHits` | damage, hits and fights (overkill also needs `TrackHealth`) |
| `TrackHealth` | health and shield over time, healing (storm damage also needs `TrackPositions`) |
| `TrackShots` | shots fired and accuracy |
| `TrackPositions` | positions, distance traveled, time in storm, supply drop distances |
| `TrackMovementDetails` | speed, rotation and states for every stored position (off in `StatsOnly` and `Full` too) |
| `TrackWeapons` | weapons each player held |
| `TrackWeaponItems` | exact item and rarity of the weapons (`HitEvent.WeaponItem`, `WeaponShots.Items`) |
| `TrackBuilds` | built pieces and materials used |
| `TrackGameplayCues` | gameplay cues (effects like healing or sliding) in the timeline |
| `CollectTimeline` | the full timeline of every update (biggest cost) |
| `PositionInterval` | seconds between two stored positions, default 1, 0 = every update |
| `IgnoredExportGroups` | further export group types of the library that should not be parsed |

Players, placements, knocks and eliminations are always read.

### The timeline

With `CollectTimeline` (`ExportSettings.Full`) every update of the replay is stored as a typed object in `export.Timeline`.
Use the typed properties instead of `OfType`, IntelliSense shows all of them:

```csharp
using FortniteReplayReader.Models.NetFieldExports;
using MoreFortniteReplayDecompression.TimelineEvents;

ReplayExport export = ReplayExporter.Read("your-amazing-fortnite.replay", ExportSettings.Full);

// Updates of one export group class, already typed. Only the values sent in this update are set, the rest is null.
foreach (var (t, channel, health) in export.Of<HealthSet>())
{
    Console.WriteLine($"{t:F1}s channel {channel.Value}: {health.HealthCurrentValue} hp, {health.ShieldCurrentValue} shield");
}

// Pawn movement (position, rotation, velocity, view direction, ...)
foreach (MovementEvent movement in export.Movements.Take(10))
{
    Console.WriteLine($"{movement.T:F1}s {movement.PawnType}: {movement.ReplicatedMovement?.Location}");
}

// Actors that appeared, with their class
foreach (ChannelOpenedEvent opened in export.ChannelsOpened.Take(10))
{
    Console.WriteLine($"{opened.T:F1}s: {opened.Archetype}");
}
```

Also there: `export.Exports` (all group updates), `export.NetDeltas` (list element updates like stat counters, `DeltasOf<T>()`
for one class), `export.ChannelsClosed` and `export.Damage` (health and shield decreases).
The movement of the pawns is split off the `PlayerPawn` updates into the small `MovementEvent`; `Of<PlayerPawn>()` has the other
pawn fields (weapon, crouched, ...) with the movement fields empty.

### Many replays at once

`ReplayExporter.ReadMany` reads several replays at the same time. The results come back in the order of the paths, and a replay
that cannot be read is returned with its error instead of stopping the others.

```csharp
List<ReplayReadResult> results = ReplayExporter.ReadMany(
    Directory.EnumerateFiles("replays", "*.replay"),
    ExportSettings.StatsOnly,
    new ParallelReadOptions { MaxParallelReplays = 8 });

foreach (ReplayReadResult result in results)
{
    Console.WriteLine(result.Success
        ? $"{result.Path}: {result.Export!.Players.Count} players in {result.Duration.TotalSeconds:F1} s"
        : $"{result.Path}: {result.Error!.Message}");
}
```

By default all read replays stay in memory until `ReadMany` returns. For many or large replays, take what you
need from each replay as soon as it is read and let it go:

- `OnReplayRead` is your own function. `ReadMany` calls it once for every replay, right after that replay is read, also for
  replays that failed. It is called from several threads at once, so use `lock` when you add up values.
- `KeepResults = false` drops every replay after `OnReplayRead`, so only the replays that are currently being read are in memory.
  The returned list then only has the path, the error and the duration of each replay.

```csharp
double damage = 0;
object sync = new();

ReplayExporter.ReadMany(paths, ExportSettings.StatsOnly, new ParallelReadOptions
{
    KeepResults = false,
    OnReplayRead = result =>
    {
        PlayerStats? player = result.Export?.Player("Detect Bergii yh");
        lock (sync) damage += player?.DamageDealt ?? 0;
    },
});
```

| Option | Default | |
|---|---|---|
| `MaxParallelReplays` | number of cores | how many replays are read at the same time (lower it if the memory runs out) |
| `LargestFirst` | `true` | start with the largest files, so no large file runs alone at the end |
| `OnReplayRead` | none | your function, called for every replay as soon as it is read |
| `KeepResults` | `true` | `false`: drop every replay after `OnReplayRead` |
| `ContinueOnError` | `true` | `false`: the first error stops the reading and is thrown |
| `CancellationToken` | none | stop starting new replays (replays already being read are finished) |

## Performance tips

Measured on 16 cores with tournament replays (client replay about 20 MB, server replay about 80 MB).

### Turn on only what you need

| Server replay, 80 MB | Time |
|---|---|
| `ExportSettings.Full` (with timeline) | 2.0 s |
| `ExportSettings.StatsOnly` | 0.86 s |
| only `TrackHits` and `TrackHealth` | 0.56 s |

The timeline is by far the biggest cost in time and memory, only turn on `CollectTimeline` if you need it.
With `TrackPositions` and `TrackBuilds` both off, the movement of the pawns is not decoded at all, which saves about a quarter
of the read time.

### Many replays: server garbage collector

Switch on the server garbage collector in your program's .csproj. It makes parallel reading almost twice as fast
(40 replays: 4.5 s instead of 8.2 s) and needs a bit more memory:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

More parallel replays than cores do not help: 32 and 64 at the same time were as fast as 16.

### One replay per program start: no tiered compilation

The first read of a program also compiles the code, which takes about a second. If your program starts, reads one replay and
exits, compile everything optimized right away (first read about 1.0 s instead of 2.1 s):

```xml
<PropertyGroup>
  <TieredCompilation>false</TieredCompilation>
</PropertyGroup>
```

Don't combine it with many replays: there it makes reading slower (6.9 s instead of 4.5 s for 40 replays), because the normal
tiered compilation optimizes the code better over time. ReadyToRun did not help in our tests.

### Memory with many replays

With `KeepResults = false` the memory stays the same however many replays you read (about 1 GB at the peak for 32 server
replays at the same time). With `KeepResults = true` every result stays in memory until the end: about 6 MB per server replay with
the stats, a lot more with the timeline. For thousands of replays use `KeepResults = false` and `OnReplayRead`.

### Measure in Release

Debug builds are several times slower (300 server replays: 268 s in Debug, 41 s in Release). Start benchmarks with
"Start Without Debugging" (Ctrl+F5) in Visual Studio, an attached debugger slows the code down too.

### Query the timeline once

`export.Of<T>()`, `export.Movements` and the other timeline properties go through the whole timeline every time
(about 55 ms for a server replay with a million entries). Store the result if you use it more than once:

```csharp
var health = export.Of<HealthSet>().ToList(); // once

foreach (PlayerStats p in export.Players)
{
    var mine = health.Where(h => h.Channel == p.Channel).ToList();
}
```

### Filter with the metadata first

`ReplayExporter.ReadMetadata` takes a few milliseconds. Use it to skip replays you don't need before reading them fully.

## What the replay does not contain

Some things are simply not in the replay file, so this fork can't give them to you:

- **Inventory:** the items (and the materials a player has right now) are not recorded.
- **Farming:** how many materials a player gathered is not recorded (only Epic's own value for the player who recorded the replay).
- **Players far away (client replays only):** a replay recorded by a player only has data about players that were close to them,
  so the stats of that player and their team are the most complete. Server replays contain every player.

## Thanks
Thanks to [Shiqan](https://github.com/Shiqan) for developing the original project that does the parsing and for keeping it updated -> [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor)

## License
Licensed under the [MIT License](LICENSE).

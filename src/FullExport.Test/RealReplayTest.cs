using System.Globalization;
using System.Reflection;
using System.Text;
using MoreFortniteReplayDecompression.Export;
using MoreFortniteReplayDecompression.Players;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

/// <summary>
/// Tests on real replays. Replays are not part of the repository, so these tests are skipped unless the environment variable
/// <c>MFRD_TEST_REPLAYS</c> names one or more folders (separated by <c>;</c>) with <c>.replay</c> files.
/// <para>
/// The snapshot test writes the stats of every player into <c>FullExportSnapshots</c> next to the replays on its first run and
/// compares against them on every later run, so any change of a stat shows up. After an intended change set
/// <c>MFRD_UPDATE_SNAPSHOTS=1</c> once to write the new values.
/// </para>
/// </summary>
public class RealReplayTest
{
    private const string ReplaysVariable = "MFRD_TEST_REPLAYS";
    private const string UpdateVariable = "MFRD_UPDATE_SNAPSHOTS";

    private static List<string> Replays()
    {
        string? folders = Environment.GetEnvironmentVariable(ReplaysVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(folders), $"{ReplaysVariable} is not set");

        List<string> replays = [.. folders!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.replay"))
            .Order()];
        Assert.SkipWhen(replays.Count == 0, $"no .replay files in {folders}");
        return replays;
    }

    private static List<ReplayExport> ReadAll(List<string> replays, ExportSettings settings)
    {
        List<ReplayReadResult> results = ReplayExporter.ReadMany(replays, settings, new ParallelReadOptions { ContinueOnError = false });
        return [.. results.Select(r => r.Export!)];
    }

    [Fact]
    public void StatsMatchTheSnapshots()
    {
        List<string> replays = Replays();
        List<ReplayExport> exports = ReadAll(replays, ExportSettings.StatsOnly);
        bool update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";

        List<string> changed = [];
        List<string> created = [];
        for (int i = 0; i < replays.Count; i++)
        {
            string folder = Path.Combine(Path.GetDirectoryName(replays[i])!, "FullExportSnapshots");
            string file = Path.Combine(folder, Path.GetFileNameWithoutExtension(replays[i]) + ".txt");
            string actual = Snapshot(exports[i]);

            if (update || !File.Exists(file))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, actual);
                created.Add(file);
                continue;
            }

            string expected = File.ReadAllText(file);
            if (expected != actual)
            {
                changed.Add($"{Path.GetFileName(replays[i])}: {FirstDifference(expected, actual)}");
            }
        }

        Assert.True(changed.Count == 0, $"{changed.Count} replays changed (set {UpdateVariable}=1 to accept):\n{string.Join("\n", changed)}");
        Assert.SkipWhen(created.Count == replays.Count, $"wrote {created.Count} new snapshots, nothing to compare yet");
    }

    [Fact]
    public void ParallelReadingGivesTheSameStats()
    {
        List<string> replays = Replays().Take(4).ToList();
        List<ReplayExport> parallel = ReadAll(replays, ExportSettings.StatsOnly);

        for (int i = 0; i < replays.Count; i++)
        {
            Assert.Equal(Snapshot(ReplayExporter.Read(replays[i], ExportSettings.StatsOnly)), Snapshot(parallel[i]));
        }
    }

    [Fact]
    public void LibraryLocationsAreNotStored()
    {
        ReplayExport export = ReplayExporter.Read(Replays()[0], ExportSettings.StatsOnly);

        Assert.NotEmpty(export.Replay.PlayerData);
        Assert.All(export.Replay.PlayerData, d => Assert.Empty(d.Locations));
        Assert.Contains(export.Players, p => p.Positions.Count > 0);
    }

    [Fact]
    public void MovementDetailsFollowThePositions()
    {
        string replay = Replays()[0];
        ReplayExport without = ReplayExporter.Read(replay, ExportSettings.StatsOnly);
        ReplayExport with = ReplayExporter.Read(replay, ExportSettings.StatsOnly with { TrackMovementDetails = true });

        Assert.All(without.Players, p => Assert.Empty(p.Movement));
        Assert.Contains(with.Players, p => p.Movement.Count > 0);
        Assert.All(with.Players, p => Assert.Equal(p.Positions.Select(s => s.T), p.Movement.Select(m => m.T)));
        Assert.Contains(with.Players, p => p.Movement.Any(m => m.Has(MovementFlags.Crouched)));

        // Switching it on must not change any stat
        Assert.Equal(Snapshot(without), Snapshot(with));
    }

    [Fact]
    public void SwitchedOffPositionsAreEmpty()
    {
        ReplayExport export = ReplayExporter.Read(Replays()[0], ExportSettings.StatsOnly with { TrackPositions = false });

        Assert.All(export.Players, p =>
        {
            Assert.Empty(p.Positions);
            Assert.Equal(0, p.DistanceTraveled);
        });
    }

    [Fact]
    public void SkippedMovementDoesNotChangeHitsAndHealth()
    {
        // Without positions and builds the pawn movement is not decoded at all; nothing else may change because of that
        string replay = Replays()[0];
        ReplayExport stats = ReplayExporter.Read(replay, ExportSettings.StatsOnly);
        ReplayExport hitsOnly = ReplayExporter.Read(replay, new ExportSettings { TrackHits = true, TrackHealth = true });

        static string Values(ReplayExport export) => string.Join("\n", export.Players.Concat(export.Bots)
            .OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Channel.Value)
            .Select(p => $"{p.Name} {p.DamageDealt} {p.DamageTaken} {p.Hits} {p.CriticalHits} {p.Knocks} {p.Eliminations} {p.HealthHealed} {p.ShieldHealed} {p.OverkillDamage}"));

        Assert.Equal(Values(stats), Values(hitsOnly));
        Assert.Contains(hitsOnly.Players, p => p.DamageDealt > 0);
        Assert.All(hitsOnly.Players, p => Assert.Empty(p.Positions));
    }

    [Fact]
    public void WeaponItemsSplitTheWeapons()
    {
        ReplayExport export = ReplayExporter.Read(Replays()[0], ExportSettings.StatsOnly);
        List<PlayerStats> shooters = [.. export.Players.Where(p => p.WeaponShots.Count > 0)];

        Assert.NotEmpty(shooters);
        Assert.All(shooters, p => Assert.All(p.WeaponShots, w =>
        {
            // Every item belongs to its weapon class and adds up to at most the totals of the class
            Assert.True(w.Items.Sum(i => i.ShotsFired) <= w.ShotsFired);
            Assert.True(w.Items.Sum(i => i.Hits) <= w.Hits);
            Assert.All(w.Items, i => Assert.StartsWith("WID_", i.Item));
        }));
        Assert.Contains(shooters, p => p.WeaponShots.Any(w => w.Items.Any(i => i.Rarity is not null)));
        Assert.Contains(shooters, p => p.DamageDealtEvents.Any(h => h.WeaponItem is not null && h.WeaponRarity is not null));

        // Switched off: no items, everything else the same
        ReplayExport without = ReplayExporter.Read(Replays()[0], ExportSettings.StatsOnly with { TrackWeaponItems = false });
        Assert.All(without.Players, p => Assert.All(p.DamageDealtEvents, h => Assert.Null(h.WeaponItem)));
        Assert.All(without.Players, p => Assert.All(p.WeaponShots, w => Assert.Empty(w.Items)));
        Assert.Equal(Snapshot(export), Snapshot(without));
    }

    [Fact]
    public void MetadataOnlyMatchesTheFullRead()
    {
        string replay = Replays()[0];
        ReplayMetadata full = ReplayExporter.Read(replay, ExportSettings.StatsOnly).Metadata;
        ReplayMetadata quick = ReplayExporter.ReadMetadata(replay);

        Assert.Equal(full.GameSessionId, quick.GameSessionId);
        Assert.Equal(full.MatchStartedUtc, quick.MatchStartedUtc);
        Assert.Equal(full.Playlist, quick.Playlist);
        Assert.Equal(full.TournamentCodeName, quick.TournamentCodeName);
    }

    /// <summary>Every simple public value of every player and bot, one line per player, sorted by name.</summary>
    private static string Snapshot(ReplayExport export)
    {
        PropertyInfo[] properties = [.. typeof(PlayerStats).GetProperties()
            .Where(p => IsSimple(p.PropertyType) && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)];

        StringBuilder text = new();
        text.AppendLine(string.Join("|", properties.Select(p => p.Name)));
        foreach (PlayerStats player in export.Players.Concat(export.Bots).OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Channel.Value))
        {
            text.AppendLine(string.Join("|", properties.Select(p => Format(p.GetValue(player)))));
        }

        return text.ToString();
    }

    private static bool IsSimple(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
    }

    private static string Format(object? value) => value switch
    {
        null => "-",
        float f => f.ToString("F2", CultureInfo.InvariantCulture),
        double d => d.ToString("F2", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "-",
    };

    private static string FirstDifference(string expected, string actual)
    {
        string[] e = expected.Split('\n');
        string[] a = actual.Split('\n');
        string[] names = e[0].TrimEnd('\r').Split('|');
        for (int line = 1; line < Math.Max(e.Length, a.Length); line++)
        {
            string[] ev = line < e.Length ? e[line].TrimEnd('\r').Split('|') : [];
            string[] av = line < a.Length ? a[line].TrimEnd('\r').Split('|') : [];
            for (int i = 0; i < Math.Max(ev.Length, av.Length); i++)
            {
                string x = i < ev.Length ? ev[i] : "(none)";
                string y = i < av.Length ? av[i] : "(none)";
                if (x != y)
                {
                    return $"player line {line}, {(i < names.Length ? names[i] : "?")}: {x} -> {y}";
                }
            }
        }

        return e[0] != a[0] ? "the list of stats changed" : "?";
    }
}

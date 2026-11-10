using FortniteReplayReader.Models;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression;

/// <summary>Builds the storm phases of a match and answers where the safe zone was at a given time.</summary>
public static class Storm
{
    /// <summary>
    /// Turns the library's safe zones (world times) into <see cref="StormPhase"/>s with replay times.
    /// </summary>
    /// <param name="zones">The library's safe zones (<see cref="MapData.SafeZones"/>).</param>
    /// <param name="worldToReplay">Converts a world time (game clock) into a replay time.</param>
    internal static List<StormPhase> BuildPhases(IEnumerable<SafeZone> zones, Func<double, float> worldToReplay)
    {
        List<SafeZone> ordered = zones
            .Where(z => z.NextCenter is not null && z.FinishShrinkTime > z.StartShrinkTime)
            .GroupBy(z => z.StartShrinkTime)
            .Select(g => g.Last())
            .OrderBy(z => z.StartShrinkTime)
            .ToList();

        List<StormPhase> phases = [];
        for (int i = 0; i < ordered.Count; i++)
        {
            SafeZone zone = ordered[i];
            FVector fromCenter = i > 0
                ? phases[i - 1].ToCenter
                : zone.LastCenter ?? zone.NextCenter;

            phases.Add(new StormPhase
            {
                Index = i,
                StartT = worldToReplay(zone.StartShrinkTime),
                EndT = worldToReplay(zone.FinishShrinkTime),
                FromCenter = fromCenter,
                FromRadius = zone.Radius,
                ToCenter = zone.NextCenter,
                ToRadius = zone.NextRadius,
            });
        }

        return phases;
    }

    /// <summary>
    /// Safe zone at replay time <paramref name="t"/>. <c>null</c> before the first shrink (the first circle is not known exactly).
    /// </summary>
    public static (FVector Center, float Radius)? SafeZoneAt(IReadOnlyList<StormPhase> phases, float t)
    {
        if (phases.Count == 0 || t < phases[0].StartT)
        {
            return null;
        }

        for (int i = phases.Count - 1; i >= 0; i--)
        {
            StormPhase phase = phases[i];
            if (t < phase.StartT)
            {
                continue;
            }

            if (t >= phase.EndT)
            {
                return (phase.ToCenter, phase.ToRadius);
            }

            float a = (t - phase.StartT) / (phase.EndT - phase.StartT);
            FVector center = new(
                phase.FromCenter.X + ((phase.ToCenter.X - phase.FromCenter.X) * a),
                phase.FromCenter.Y + ((phase.ToCenter.Y - phase.FromCenter.Y) * a),
                0);

            return (center, phase.FromRadius + ((phase.ToRadius - phase.FromRadius) * a));
        }

        return null;
    }

    /// <summary><c>true</c> if <paramref name="location"/> is outside the safe zone at replay time <paramref name="t"/>.</summary>
    public static bool IsInStorm(IReadOnlyList<StormPhase> phases, float t, FVector location)
    {
        if (SafeZoneAt(phases, t) is not { } zone)
        {
            return false;
        }

        double dx = location.X - zone.Center.X;
        double dy = location.Y - zone.Center.Y;

        return (dx * dx) + (dy * dy) > (double)zone.Radius * zone.Radius;
    }
}

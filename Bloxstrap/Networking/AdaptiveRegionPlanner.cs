namespace Bloxstrap.Networking
{
    public sealed record RegionObservation
    {
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public int LocalHour { get; init; }
        public string City { get; init; } = "";
        public string Country { get; init; } = "";
        public string Route { get; init; } = "unknown";
        public bool IsCalibration { get; init; }
        public double AverageMs { get; init; }
        public double JitterMs { get; init; }
        public double IcmpLossPercent { get; init; }
    }

    public sealed record LearnedRegion(string City, double CostMs, int Samples, string Evidence);

    /// <summary>Robust, local ICMP observations; never represents UDP gameplay ping or loss.</summary>
    public static class AdaptiveRegionPlanner
    {
        public static IReadOnlyList<LearnedRegion> Rank(IEnumerable<RegionObservation> observations,
            string route, int localHour, DateTimeOffset now, bool northAmericaOnly = false, bool europeOnly = false)
        {
            var recent = observations.Where(x => x.Timestamp >= now.AddDays(-30) && x.Timestamp <= now &&
                x.Route == route && !string.IsNullOrWhiteSpace(x.City) && double.IsFinite(x.AverageMs) &&
                x.AverageMs >= 0 && double.IsFinite(x.JitterMs) && double.IsFinite(x.IcmpLossPercent) &&
                (!northAmericaOnly || RegionGeography.IsNorthAmerica(x.Country)) && (!europeOnly || RegionGeography.IsEurope(x.Country))).ToList();
            var result = new List<LearnedRegion>();
            foreach (var region in recent.GroupBy(x => x.City, StringComparer.OrdinalIgnoreCase))
            {
                var joins = region.Where(x => !x.IsCalibration).OrderByDescending(x => x.Timestamp).Take(30).ToList();
                var timed = joins.Where(x => x.LocalHour / 6 == localHour / 6).ToList();
                List<RegionObservation> used;
                string evidence;
                if (timed.Count >= 3) { used = timed; evidence = "joined servers, this time of day"; }
                else if (joins.Count >= 5) { used = joins; evidence = "joined servers, all hours"; }
                else { used = region.Where(x => x.IsCalibration).OrderByDescending(x => x.Timestamp).Take(8).ToList(); evidence = "installation routing probe (provisional)"; }
                if (used.Count == 0) continue;
                // Jitter and failed ICMP probes penalize only this supplementary measurement.
                var costs = used.Select(x => x.AverageMs + Math.Max(0, x.JitterMs) * 2 +
                    Math.Clamp(x.IcmpLossPercent, 0, 100) * 0.5).Order().ToArray();
                double median = costs.Length % 2 == 0 ? (costs[costs.Length / 2 - 1] + costs[costs.Length / 2]) / 2 : costs[costs.Length / 2];
                result.Add(new(region.Key, median, used.Count, evidence));
            }
            return result.OrderBy(x => x.CostMs).ThenBy(x => x.City, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IReadOnlyList<LearnedRegion> Stabilize(IReadOnlyList<LearnedRegion> ranked, string currentCity)
        {
            if (ranked.Count < 2) return ranked;
            var current = ranked.FirstOrDefault(x => x.City.Equals(currentCity, StringComparison.OrdinalIgnoreCase));
            if (current is null || current == ranked[0]) return ranked;
            double improvement = current.CostMs - ranked[0].CostMs;
            return improvement < Math.Max(5, current.CostMs * 0.15)
                ? new[] { current }.Concat(ranked.Where(x => x != current)).ToList() : ranked;
        }
    }
}

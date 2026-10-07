namespace Bloxstrap.Networking
{
    /// <summary>Regions from confirmed joins and public metadata, keyed by place and job.</summary>
    internal static class KnownServerRegions
    {
        internal sealed record Entry(string Region, int? DataCenterId, string Source, DateTimeOffset RecordedAt);
        internal static string FilePath => Path.Combine(Paths.Cache, "ServerRegions.json");
        private static Dictionary<string, Entry> Read()
        {
            try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? new() : new(); }
            catch (Exception ex) { App.Logger.WriteException("KnownServerRegions::Read", ex); return new(); }
        }
        internal static Dictionary<string, Entry> ForPlace(long placeId)
        {
            using var gate = NetworkHistory.Lock();
            string prefix = placeId + ":";
            return Read().Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && x.Value.RecordedAt > DateTimeOffset.UtcNow.AddDays(-7))
                .ToDictionary(x => x.Key[prefix.Length..], x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
        internal static void Remember(long placeId, IEnumerable<KeyValuePair<string, Entry>> entries, DateTimeOffset startedAt)
        {
            using var gate = NetworkHistory.Lock();
            if (!NetworkHistory.Accept(startedAt)) return;
            var all = Read();
            foreach (var (job, entry) in entries)
                if (placeId > 0 && Guid.TryParse(job, out _) && !string.IsNullOrWhiteSpace(entry.Region) && entry.Region != "Unknown")
                    all[placeId + ":" + Guid.Parse(job).ToString()] = entry;
            all = all.Where(x => x.Value.RecordedAt > DateTimeOffset.UtcNow.AddDays(-7)).OrderByDescending(x => x.Value.RecordedAt).Take(2000).ToDictionary();
            Directory.CreateDirectory(Paths.Cache);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all)); File.Move(temp, FilePath, true);
        }
        internal static void Observe(CompetitiveNetworkEvent evt)
        {
            if (evt.RegionQuality == Competitive.RegionQuality.Unknown) return;
            Remember(evt.PlaceId, new[] { new KeyValuePair<string, Entry>(evt.JobId, new(evt.Location, null, "Observed Roblox join", DateTimeOffset.UtcNow)) }, evt.Timestamp);
        }
    }
}

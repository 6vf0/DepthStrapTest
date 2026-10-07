using Bloxstrap.Integrations;
using Bloxstrap.Models;
using Bloxstrap.Models.Entities;

namespace Bloxstrap.Competitive
{
    /// <summary>Chime notifications consume the fast region event; diagnostics never delay them.</summary>
    public sealed class CompetitiveRegionMonitor : IDisposable
    {
        private const string LOG_IDENT = "CompetitiveRegionMonitor";
        private readonly CompetitiveNetworkMonitor _networkMonitor;
        private readonly object _historyLock = new();
        private bool _isDisposed;
        public event EventHandler<CompetitiveRegionResult>? OnRegionEvaluated;

        public CompetitiveRegionMonitor(CompetitiveNetworkMonitor networkMonitor)
        {
            _networkMonitor = networkMonitor;
            _networkMonitor.RegionResolved += OnRegionResolved;
        }

        public bool IsChimeCandidate(ActivityData data) =>
            data.UniverseId == CompetitiveRegionService.DeepwokenUniverseId &&
            data.IsTeleport && data.ServerType == ServerType.Reserved;

        private void OnRegionResolved(object? sender, CompetitiveNetworkEvent evt)
        {
            if (_isDisposed) return;
            var result = new CompetitiveRegionResult
            {
                Timestamp = evt.Timestamp,
                UniverseId = evt.UniverseId,
                PlaceId = evt.PlaceId,
                JobId = evt.JobId,
                IsTeleport = evt.IsTeleport,
                IsReservedServer = evt.IsReservedServer,
                IsDeepwoken = evt.IsDeepwoken,
                ServerAddress = evt.MachineAddress,
                Location = evt.Location,
                RegionSource = evt.RegionSource,
                Quality = evt.RegionQuality,
                Score = evt.RegionScore,
                IsPreferred = evt.IsPreferredRegion
            };
            lock (_historyLock) SaveHistoryEntry(result);
            _ = CompetitiveSessionLogger.WriteBadRegionAsync(result);
            OnRegionEvaluated?.Invoke(this, result);
        }

        private void SaveHistoryEntry(CompetitiveRegionResult result)
        {
            if (!App.Settings.Prop.LogCompetitiveSessions || !App.Settings.Prop.CompetitiveModeEnabled)
                return;

            try
            {
                using var resetLock = Networking.NetworkHistory.Lock();
                if (!Networking.NetworkHistory.Accept(result.Timestamp)) return;
                string path = Path.Combine(Paths.Cache, "CompetitiveRegionHistory.json");

                List<CompetitiveSessionEntry> history = new();
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<List<CompetitiveSessionEntry>>(File.ReadAllText(path));
                    if (loaded is not null)
                        history = loaded;
                }

                history.Add(new CompetitiveSessionEntry
                {
                    Timestamp = result.Timestamp,
                    UniverseId = result.UniverseId,
                    PlaceId = result.PlaceId,
                    JobId = result.JobId,
                    ServerType = result.IsReservedServer ? "Reserved" : (result.IsTeleport ? "Public-Teleport" : "Public"),
                    IsTeleport = result.IsTeleport,
                    MachineAddress = result.ServerAddress,
                    Location = result.Location,
                    PreferredRegion = App.Settings.Prop.CompetitivePreferredCity,
                    Quality = result.Quality.ToString(),
                    Score = result.Score,
                    RegionSource = result.RegionSource,
                    IsDeepwoken = result.IsDeepwoken,
                });

                // keep the file bounded: newest 500 entries
                if (history.Count > 500)
                    history = history.TakeLast(500).ToList();

                Directory.CreateDirectory(Paths.Cache);
                File.WriteAllText(path, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::SaveHistoryEntry", ex);
            }
        }

        /// <summary>
        /// Builds the tray notification for an evaluated join. Returns null when no alert is wanted.
        /// Unknown regions never produce a "REGION PULL" - evidence first, drama later.
        /// </summary>
        public static (string Title, string Message)? BuildNotification(CompetitiveRegionResult result)
        {
            if (!result.IsDeepwoken)
                return null; // other games: log only, no tray spam

            bool chime = result.IsTeleport && result.IsReservedServer;

            // Initial (non-Chime) Deepwoken joins are mostly logged; only a bad lobby region
            // earns an early balloon (your Chime will likely be pulled to the same area).
            if (!chime && result.Quality is not (RegionQuality.Poor or RegionQuality.Bad))
                return null;

            switch (result.Quality)
            {
                case RegionQuality.Ideal:
                    return chime
                        ? ("CHIME REGION", $"{result.Location}\nPreferred region ✓\n{ServerTypeLine(result)}")
                        : ($"DEEPWOKEN - {result.Location}", $"Preferred region ✓\n{ServerTypeLine(result)}");

                case RegionQuality.Excellent:
                case RegionQuality.Good:
                    return chime
                        ? ("CHIME REGION", $"{result.Location}\nFallback region\n{ServerTypeLine(result)}")
                        : ($"DEEPWOKEN - {result.Location}", $"Fallback region\n{ServerTypeLine(result)}");

                case RegionQuality.Acceptable:
                    return chime
                        ? ("CHIME REGION", $"{result.Location}\nWithin preferred area\n{ServerTypeLine(result)}")
                        : ($"DEEPWOKEN - {result.Location}", $"Within preferred area\n{ServerTypeLine(result)}");

                case RegionQuality.Poor:
                case RegionQuality.Bad:
                    return chime
                        ? ("NON-PREFERRED SERVER", $"{result.Location}\nNon-preferred Chime server\nTarget: {App.Settings.Prop.CompetitivePreferredCity}  |  Actual: {result.Location}")
                        : ($"DEEPWOKEN - {result.Location}", $"Non-preferred region (score {result.Score})\n{ServerTypeLine(result)}");

                default: // Unknown - evidence first, no drama
                    if (!chime)
                        return null;

                    string unknownLoc = string.IsNullOrEmpty(result.Location) ? "Unknown" : result.Location;
                    return ("CHIME REGION", $"{unknownLoc}\nRegion quality: Unknown\n{ServerTypeLine(result)}");
            }

            static string ServerTypeLine(CompetitiveRegionResult r) =>
                r.IsReservedServer ? "Reserved server" : (r.IsTeleport ? "Teleport join" : "Public server");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _networkMonitor.RegionResolved -= OnRegionResolved;
            GC.SuppressFinalize(this);
        }
    }
}

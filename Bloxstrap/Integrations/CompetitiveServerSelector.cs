using Bloxstrap.Competitive;
using System.Net.Http.Json;
using Bloxstrap.RobloxInterfaces;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Phase-2 feature: when a plain Deepwoken launch URI arrives without an explicit server ID,
    /// search public servers and replace the URI with the best region candidate.
    ///
    /// Only touches launches we fully control (external roblox://experiences/start URIs).
    /// Never interferes with private servers, access codes, explicit gameInstanceId joins,
    /// internal Roblox teleports or Studio.
    /// </summary>
    public static class CompetitiveServerSelector
    {
        private const string LOG_IDENT = "CompetitiveServerSelector";

        private sealed class PlaceUniverseResponse
        {
            [JsonPropertyName("universeId")]
            public long? UniverseId { get; set; }
        }

        /// <summary>Path of the pending-join handoff file (bootstrapper -> watcher monitor).</summary>
        public static string PendingJoinFilePath => Path.Combine(Paths.Cache, "CompetitivePendingJoin.json");

        public sealed class PendingJoinFile
        {
            [JsonPropertyName("jobId")] public string JobId { get; set; } = "";
            [JsonPropertyName("dataCenterId")] public int? DataCenterId { get; set; }
            [JsonPropertyName("placeId")] public long PlaceId { get; set; }
            [JsonPropertyName("region")] public string Region { get; set; } = "";
            [JsonPropertyName("score")] public int Score { get; set; }
            [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Returns a replacement launch command line, or null when the URI should be left alone.
        /// Bounded by an overall time budget so launch never feels slow.
        /// </summary>
        public static async Task<string?> TrySelectPreferredServerAsync(string launchArgs)
        {
            var settings = App.Settings.Prop;

            if (!settings.CompetitiveModeEnabled || !settings.PreferredRegionEnabled || !settings.AutoSelectPreferredServerOnLaunch)
                return null;

            // overall budget: the whole interception must stay snappy
            using var budgetCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.PreferredRegionSearchTimeoutSeconds, 3, 120)));
            var token = budgetCts.Token;

            try
            {
                var uri = RobloxLaunchUri.TryParse(launchArgs);
                if (uri is null || uri.PlaceId is null)
                    return null; // not an experiences/start URI we understand

                long placeId = uri.PlaceId.Value;

                // explicit server id or private server -> never touch
                if (uri.HasExplicitServerId || uri.HasParameter("accessCode") ||
                    uri.HasParameter("privateServerLinkCode") || uri.HasParameter("reservedServerAccessCode"))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"URI already pins a server/private code for place {placeId}, skipping");
                    return null;
                }

                // --- identify Deepwoken by universe id (not by one hardcoded place id) ---
                long? universeId = await ResolveUniverseIdAsync(placeId, token);
                if (universeId is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not resolve universe for place {placeId}, skipping");
                    return null;
                }

                if (universeId != CompetitiveRegionService.DeepwokenUniverseId)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Place {placeId} is universe {universeId} (not Deepwoken), skipping");
                    return null;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Deepwoken launch detected for place {placeId}, searching public servers...");
                CompetitiveSessionLogger.Write($"Auto-select: Deepwoken place={placeId}");
                await Networking.AdaptiveRegionService.RefreshAsync(token);
                if (string.IsNullOrWhiteSpace(settings.CompetitivePreferredCity)) return null;

                // --- search + rank (bounded by the same scan limits as the Region Selector) ---
                var fetcher = new DepthStrapServerBrowser();
                await CompetitiveRegionService.EnsureRegistryLoadedAsync(fetcher).WaitAsync(token);

                int maxPages = Math.Clamp(settings.PreferredRegionMaxPages, 1, 50);
                var deadline = DateTime.Now.AddSeconds(Math.Clamp(settings.PreferredRegionSearchTimeoutSeconds, 3, 120));

                ServerInstance? best = null;
                RegionClassification? bestCls = null;
                int pagesChecked = 0;
                string cursor = "";

                while (pagesChecked < maxPages && DateTime.Now < deadline)
                {
                    var result = await fetcher.FetchServerInstancesAsync(placeId, cursor, sortOrder: 2, cancellationToken: token);
                    if (result is null)
                        break;

                    foreach (var s in result.Servers)
                    {
                        var cls = CompetitiveRegionService.Classify(
                            s.Region, s.DataCenterId,
                            source: s.DataCenterId.HasValue ? "Roblox DataCenterId" : s.RegionSource);
                        if (cls.Quality == RegionQuality.Unknown) continue;

                        if (best is null ||
                            cls.Score > bestCls!.Score ||
                            (cls.Score == bestCls.Score && s.Playing < best.Playing))
                        {
                            best = s;
                            bestCls = cls;
                        }
                    }

                    cursor = result.NextCursor;
                    pagesChecked++;

                    if (bestCls is not null && bestCls.Score >= 95 || string.IsNullOrWhiteSpace(cursor))
                        break; // ideal found or no more pages
                }

                if (best is null || bestCls is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No public server candidates found, keeping default join");
                    return null;
                }

                // don't force a bad region on the user: Acceptable (50+) or better only
                if (bestCls.Score < 50)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Best candidate scored {bestCls.Score} (< 50), keeping default join");
                    CompetitiveSessionLogger.Write($"Auto-select skipped: best score {bestCls.Score}");
                    return null;
                }

                // --- handoff to the watcher's region monitor (DC id we already know) ---
                try
                {
                    Directory.CreateDirectory(Paths.Cache);
                    File.WriteAllText(PendingJoinFilePath, JsonSerializer.Serialize(new PendingJoinFile
                    {
                        JobId = best.Id,
                        DataCenterId = best.DataCenterId,
                        PlaceId = placeId,
                        Region = bestCls.DisplayName,
                        Score = bestCls.Score,
                        Timestamp = DateTime.Now
                    }));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Pending-join handoff write failed: {ex.Message}");
                }

                uri.SetParameter("gameInstanceId", best.Id);
                string newArgs = uri.Build();

                App.Logger.WriteLine(LOG_IDENT,
                    $"Selected server {best.Id} ({bestCls.DisplayName}, score {bestCls.Score}, {best.Playing}/{best.MaxPlayers}) after {pagesChecked} page(s)");

                CompetitiveSessionLogger.Write(
                    $"Auto-select: server={best.Id} region={bestCls.DisplayName} score={bestCls.Score} pages={pagesChecked}");

                return newArgs;
            }
            catch (OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Time budget exceeded, keeping default join");
                CompetitiveSessionLogger.Write("Auto-select: time budget exceeded, default join kept");
                return null;
            }
            catch (Exception ex)
            {
                // interception is a bonus - any failure falls back to the normal launch path
                App.Logger.WriteException($"{LOG_IDENT}::TrySelectPreferredServerAsync", ex);
                CompetitiveSessionLogger.Write($"Auto-select failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<long?> ResolveUniverseIdAsync(long placeId, CancellationToken token)
        {
            try
            {
                using var response = await App.HttpClient.GetAsync(
                    $"https://apis.roblox.com/universes/v1/places/{placeId}/universe",
                    HttpCompletionOption.ResponseHeadersRead, token);

                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadFromJsonAsync<PlaceUniverseResponse>(cancellationToken: token);
                return body?.UniverseId;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Universe lookup failed for place {placeId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Loads the pending-join handoff (called by CompetitiveRegionMonitor in the watcher process).</summary>
        public static PendingJoinFile? LoadPendingJoin()
        {
            try
            {
                if (!File.Exists(PendingJoinFilePath))
                    return null;

                var entry = JsonSerializer.Deserialize<PendingJoinFile>(File.ReadAllText(PendingJoinFilePath));

                // only trust very recent handoffs (a stale file from a crashed session is useless)
                if (entry is not null && DateTime.Now - entry.Timestamp > TimeSpan.FromMinutes(10))
                    return null;

                return entry;
            }
            catch
            {
                return null;
            }
        }
    }
}

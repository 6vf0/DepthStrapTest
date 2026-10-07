using Bloxstrap.Competitive;
namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Competitive session logging, two formats:
    /// 1. human-readable daily log:   Logs/CompetitiveSession-YYYYMMDD.log
    /// 2. machine-readable JSONL:     Logs/CompetitiveSessions/YYYY-MM-DD.jsonl (one event per line,
    ///    append-friendly, corruption-resistant, easy to import into Python for A/B analysis)
    /// This is the evidence trail for "was this bad match a region problem, a frame-time problem, or CPU contention?"
    /// Never throws; logging failure must not affect gameplay or launch.
    /// </summary>
    internal static class CompetitiveSessionLogger
    {
        private static readonly object _lock = new();
        private static readonly SemaphoreSlim _jsonlLock = new(1, 1);

        public static string GetLogPath() =>
            Path.Combine(Paths.Logs, $"CompetitiveSession-{DateTime.Now:yyyyMMdd}.log");

        public static string GetSessionsDir() =>
            Path.Combine(Paths.Logs, "CompetitiveSessions");

        public static string GetJsonlPath() =>
            Path.Combine(GetSessionsDir(), $"{DateTime.Now:yyyy-MM-dd}.jsonl");

        public static void Write(string message)
        {
            if (!App.Settings.Prop.LogCompetitiveSessions || !App.Settings.Prop.CompetitiveModeEnabled)
                return;

            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

                lock (_lock)
                {
                    using var resetLock = Networking.NetworkHistory.Lock();
                    using var processLock = Bloxstrap.Networking.NetworkHistory.DataLock("CompetitiveTextLog", TimeSpan.FromSeconds(2));
                    if (!processLock.IsAcquired) return;
                    Directory.CreateDirectory(Paths.Logs);
                    File.AppendAllText(GetLogPath(), line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // last-resort fallback to the main log; never propagate
                try { App.Logger.WriteLine("CompetitiveSessionLogger", $"Failed writing session log: {ex.Message}"); }
                catch { /* truly give up */ }
            }
        }

        #region JSONL history

        /// <summary>
        /// Session metadata header, written once per Roblox launch (before server events follow).
        /// Makes A/B experiments reproducible: which app version, channel and settings produced each run.
        /// </summary>
        public static async Task WriteSessionHeaderAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled())
                return;

            try
            {
                var s = App.Settings.Prop;

                // WARP state at launch (cached value when the monitor already queried, else best-effort fresh query)
                string warp = "unknown", colo = "";
                var cf = CloudflareNetworkState.Cached;
                if (cf is null && s.CompetitiveCloudflareDetectionEnabled)
                    cf = await CloudflareNetworkState.QueryAsync(cancellationToken);
                if (cf is not null) { warp = cf.Warp; colo = cf.Colo; }

                var header = new JsonlSessionHeader
                {
                    Timestamp = DateTime.Now,
                    AppVersion = App.Version,
                    RobloxChannel = s.Channel,
                    RobloxVersionGuid = SafeVersionGuid(),
                    CompetitiveMode = s.CompetitiveModeEnabled,
                    PreferredRegion = s.CompetitivePreferredCity,
                    FpsCap = s.CompetitiveFpsCap,
                    ProcessPriority = s.CompetitiveProcessPriority.ToString(),
                    RunLabel = s.CompetitiveRunLabel,
                    Warp = warp,
                    Colo = colo
                };

                await AppendJsonlAsync(header, cancellationToken);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("CompetitiveSessionLogger::WriteSessionHeader", ex);
            }
        }

        /// <summary>Appends one network event line. Concurrent-safe (lines never interleave).</summary>
        public static async Task WriteNetworkEventAsync(CompetitiveNetworkEvent evt, CancellationToken cancellationToken = default)
        {
            if (!Enabled())
                return;

            try
            {
                bool fullEgressIp = App.Settings.Prop.StoreFullEgressIpInLogs;

                var line = new JsonlEvent
                {
                    Timestamp = evt.Timestamp,
                    RunLabel = evt.RunLabel,
                    UniverseId = evt.UniverseId,
                    PlaceId = evt.PlaceId,
                    JobId = evt.JobId,
                    IsTeleport = evt.IsTeleport,
                    Reserved = evt.IsReservedServer,
                    MachineIp = evt.MachineAddress,
                    UdmuxIp = evt.UdmuxAddress ?? "",
                    UdmuxPort = evt.UdmuxPort,
                    RccIp = evt.RccAddress ?? "",
                    RccPort = evt.RccPort,
                    Location = evt.Location,
                    RegionQuality = evt.RegionQuality.ToString(),
                    RegionScore = evt.RegionScore,
                    RegionSource = evt.RegionSource,
                    CloudflareWarp = evt.Cloudflare?.WarpActive,
                    CloudflareColo = evt.Cloudflare?.Colo ?? "",
                    EgressIp = evt.Cloudflare is null ? "" : (fullEgressIp ? evt.Cloudflare.PublicIp : evt.Cloudflare.MaskedPublicIp),
                    IcmpSent = evt.Latency?.Sent,
                    IcmpReceived = evt.Latency?.Received,
                    IcmpLossPct = evt.Latency?.HasMeasurement == true ? evt.Latency.LossPercent : null,
                    IcmpMinMs = evt.Latency?.MinimumMs,
                    IcmpAvgMs = evt.Latency?.AverageMs,
                    IcmpMaxMs = evt.Latency?.MaximumMs,
                    IcmpJitterMs = evt.Latency?.JitterMs,
                    IsDeepwoken = evt.IsDeepwoken,
                    PossibleChime = evt.IsPossibleChime,
                    TracerouteFile = evt.TracerouteFile
                };

                await AppendJsonlAsync(line, cancellationToken);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("CompetitiveSessionLogger::WriteNetworkEvent", ex);
            }
        }

        public static async Task WriteBadRegionAsync(Models.CompetitiveRegionResult result)
        {
            if (!Enabled() || !result.IsDeepwoken || result.Quality is not (RegionQuality.Poor or RegionQuality.Bad)) return;
            try
            {
                await AppendJsonlAsync(new JsonlEvent
                {
                    Type = "bad_region", Timestamp = result.Timestamp, JobId = result.JobId,
                    UniverseId = result.UniverseId, PlaceId = result.PlaceId, Location = result.Location,
                    RegionQuality = result.Quality.ToString(), RegionScore = result.Score, RegionSource = result.RegionSource,
                    PreferredRegion = App.Settings.Prop.CompetitivePreferredCity, IsDeepwoken = true,
                    IsTeleport = result.IsTeleport, Reserved = result.IsReservedServer
                }, CancellationToken.None);
                using var resetLock = Networking.NetworkHistory.Lock();
                if (Networking.NetworkHistory.Accept(result.Timestamp))
                    Write($"Bad region: target={App.Settings.Prop.CompetitivePreferredCity}; actual={result.Location}; quality={result.Quality}; job={result.JobId}");
            }
            catch (Exception ex) { App.Logger.WriteException("CompetitiveSessionLogger::BadRegion", ex); }
        }
        private static bool Enabled() =>
            App.Settings.Prop.CompetitiveModeEnabled && App.Settings.Prop.LogCompetitiveSessions;

        private static string SafeVersionGuid()
        {
            try { return App.PlayerState?.Prop?.VersionGuid ?? ""; }
            catch { return ""; }
        }

        private static async Task AppendJsonlAsync(object payload, CancellationToken cancellationToken)
        {
            await _jsonlLock.WaitAsync(cancellationToken);
            try
            {
                using var resetLockJson = Networking.NetworkHistory.Lock();
                if (payload is JsonlEvent evt && !Networking.NetworkHistory.Accept(evt.Timestamp)) return;
                using var processLock = Bloxstrap.Networking.NetworkHistory.DataLock("CompetitiveJsonLog", TimeSpan.FromSeconds(2));
                if (!processLock.IsAcquired) throw new IOException("Session log is busy in another process.");
                string json = JsonSerializer.Serialize(payload, JsonlOptions);
                Directory.CreateDirectory(GetSessionsDir());
                File.AppendAllText(GetJsonlPath(), json + Environment.NewLine);
            }
            finally
            {
                _jsonlLock.Release();
            }
        }

        private static readonly JsonSerializerOptions JsonlOptions = new()
        {
            PropertyNamingPolicy = null, // DTOs use explicit [JsonPropertyName]
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>Session header line (type: "session").</summary>
        private sealed class JsonlSessionHeader
        {
            [System.Text.Json.Serialization.JsonPropertyName("type")] public string Type { get; set; } = "session";
            [System.Text.Json.Serialization.JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("robloxChannel")] public string RobloxChannel { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("robloxVersionGuid")] public string RobloxVersionGuid { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("competitiveMode")] public bool CompetitiveMode { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("preferredRegion")] public string PreferredRegion { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("fpsCap")] public int FpsCap { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("processPriority")] public string ProcessPriority { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("runLabel")] public string RunLabel { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("warp")] public string Warp { get; set; } = "unknown";
            [System.Text.Json.Serialization.JsonPropertyName("colo")] public string Colo { get; set; } = "";
        }

        /// <summary>Server event line (type: "event"). One JSON object per Roblox server join.</summary>
        private sealed class JsonlEvent
        {
            [System.Text.Json.Serialization.JsonPropertyName("type")] public string Type { get; set; } = "event";
            [System.Text.Json.Serialization.JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("runLabel")] public string RunLabel { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("universeId")] public long UniverseId { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("placeId")] public long PlaceId { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("jobId")] public string JobId { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("isTeleport")] public bool IsTeleport { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("reserved")] public bool Reserved { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("machineIp")] public string MachineIp { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("udmuxIp")] public string UdmuxIp { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("udmuxPort")] public int? UdmuxPort { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("rccIp")] public string RccIp { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("rccPort")] public int? RccPort { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("location")] public string Location { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("preferredRegion")] public string PreferredRegion { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("regionQuality")] public string RegionQuality { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("regionScore")] public int RegionScore { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("regionSource")] public string RegionSource { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("cloudflareWarp")] public bool? CloudflareWarp { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("cloudflareColo")] public string CloudflareColo { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("egressIp")] public string EgressIp { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("icmpSent")] public int? IcmpSent { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpReceived")] public int? IcmpReceived { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpLossPct")] public double? IcmpLossPct { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpMinMs")] public double? IcmpMinMs { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpAvgMs")] public double? IcmpAvgMs { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpMaxMs")] public double? IcmpMaxMs { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("icmpJitterMs")] public double? IcmpJitterMs { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("isDeepwoken")] public bool IsDeepwoken { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("possibleChime")] public bool PossibleChime { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("tracerouteFile")] public string TracerouteFile { get; set; } = "";
        }

        #endregion
    }
}

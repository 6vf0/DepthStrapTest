using Bloxstrap.Roblox;
using Bloxstrap.Integrations;
using System.Collections.Concurrent;
using Bloxstrap.Models;
using Bloxstrap.Models.Entities;

namespace Bloxstrap.Competitive
{
    /// <summary>
    /// Central network orchestration service (RobloxRouteLab integration).
    ///
    /// Flow per confirmed join:
    ///   ActivityWatcher.OnGameJoin
    ///     -> immutable snapshot copy (never hold mutable Data across awaits)
    ///     -> dedupe on JobId|liveEndpoint|PlaceId
    ///     -> Task.Run: location resolve -> classify -> Cloudflare trace -> ICMP -> optional traceroute
    ///     -> JSONL history line -> RegionResolved (stage 1, fast) / DiagnosticsCompleted (stage 2)
    ///
    /// Responsibilities kept OUT of ActivityWatcher on purpose:
    ///   - ActivityWatcher = Roblox log interpretation only
    ///   - this class      = orchestration + measurement pipeline
    ///   - CompetitiveRegionService = region quality
    ///   - CompetitiveNetworkDiagnostics = ping/traceroute/WARP plumbing
    ///   - CompetitiveSessionLogger = history files
    ///
    /// Coexists with CompetitiveRegionMonitor (Task A): that one owns classification-driven
    /// tray notifications; this one owns the live-endpoint diagnostics + JSONL evidence trail.
    /// Everything fails independently - a dead API must never touch gameplay.
    /// </summary>
    public sealed class CompetitiveNetworkMonitor : IDisposable
    {
        private const string LOG_IDENT = "CompetitiveNetworkMonitor";

        private readonly ActivityWatcher _activityWatcher;
        private readonly CompetitiveNetworkDiagnostics _diagnostics = new();

        // RouteLab parity dedupe key: JobId | live endpoint | PlaceId
        private readonly ConcurrentDictionary<string, byte> _seenEvents = new();

        // rapid teleports must not spawn dozens of simultaneous HTTP/ping operations
        private readonly SemaphoreSlim _diagSemaphore = new(2, 2);

        // latest evaluated event per job (UI session block + notification enrichment)
        private readonly Dictionary<string, CompetitiveNetworkEvent> _latestByJob = new();
        private readonly object _latestLock = new();
        private CompetitiveNetworkEvent? _lastEvent;
        private DateTime _activeJoinTimestamp;

        private readonly CancellationTokenSource _cts = new();
        private readonly CancellationToken _token;
        private CompetitiveServerSelector.PendingJoinFile? _pendingJoin;
        private bool _isDisposed = false;

        /// <summary>Stage 1: region resolved (fast path - UI can show the Chime balloon immediately).</summary>
        public event EventHandler<CompetitiveNetworkEvent>? RegionResolved;
        internal Func<CompetitiveNetworkEvent, Task<bool>>? AutoLogHandler { get; set; }
        internal Func<string, CancellationToken, Task<(string Location, string Source)>> LocationLookup { get; set; } = QueryLocationAsync;
        internal Func<long, CancellationToken, Task<long?>> UniverseLookup { get; set; } = CompetitiveServerSelector.ResolveUniverseIdAsync;
        internal TimeSpan LocationRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
        internal TimeSpan UniverseLogWait { get; set; } = TimeSpan.FromSeconds(15);
        internal Task EvaluationTask { get; private set; } = Task.CompletedTask;

        /// <summary>Stage 2: ICMP/WARP/traceroute finished and the JSONL line is on disk.</summary>
        public event EventHandler<CompetitiveNetworkEvent>? DiagnosticsCompleted;

        public CompetitiveNetworkMonitor(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _token = _cts.Token;
            _pendingJoin = CompetitiveServerSelector.LoadPendingJoin();

            _activityWatcher.OnGameJoin += OnGameJoin;
            _activityWatcher.OnConnectionUpdated += OnGameJoin;
            _activityWatcher.OnGameLeave += OnGameLeave;

            // session metadata header (one per Roblox launch); also warms the Cloudflare cache
            _ = Task.Run(() => CompetitiveSessionLogger.WriteSessionHeaderAsync(_token));

            App.Logger.WriteLine(LOG_IDENT, "Competitive network monitor attached");
        }

        private void OnGameJoin(object? sender, EventArgs e)
        {
            if (_isDisposed || !_activityWatcher.InGame)
                return;

            // snapshot BEFORE any await - the watcher may mutate Data again during another teleport.
            // dataRef is stable for this join's lifetime (each join gets a fresh ActivityData instance).
            var snapshot = ActivitySnapshot.From(_activityWatcher.Data);
            Task<long> universeResolved = _activityWatcher.Data.UniverseResolved;

            string key = BuildKey(snapshot);
            if (!_seenEvents.TryAdd(key, 0))
                return; // same server endpoint already evaluated - no duplicate spam on rapid teleports

            TrimSeen();
            lock (_latestLock) _activeJoinTimestamp = snapshot.Timestamp;

            // Show the live join immediately; universe/location services enrich it asynchronously.
            RecordLatest(new CompetitiveNetworkEvent
            {
                ProcessId = _activityWatcher.RobloxProcessId,
                Timestamp = snapshot.Timestamp, UniverseId = snapshot.UniverseId, PlaceId = snapshot.PlaceId, JobId = snapshot.JobId,
                MachineAddress = snapshot.MachineAddress, UdmuxAddress = snapshot.UdmuxAddress, UdmuxPort = snapshot.UdmuxPort,
                RccAddress = snapshot.RccAddress, RccPort = snapshot.RccPort, IsTeleport = snapshot.IsTeleport,
                IsReservedServer = snapshot.ServerType == ServerType.Reserved, RegionQuality = RegionQuality.Unknown, RegionSource = "Unknown",
                IsDeepwoken = snapshot.UniverseId == CompetitiveRegionService.DeepwokenUniverseId, Cloudflare = CloudflareNetworkState.Cached
            });

            EvaluationTask = Task.Run(() => ProcessAsync(snapshot, universeResolved));
        }

        private static string BuildKey(ActivitySnapshot snapshot) =>
            $"{snapshot.JobId}|{snapshot.LiveEndpoint}|{snapshot.PlaceId}";

        private void TrimSeen()
        {
            // bound the dedupe set for very long sessions (newest wins; a re-join of an ancient server is acceptable)
            if (_seenEvents.Count > 256)
            {
                foreach (var key in _seenEvents.Keys.Take(128))
                    _seenEvents.TryRemove(key, out _);
            }
        }

        private async Task ProcessAsync(ActivitySnapshot snapshot, Task<long> universeResolved)
        {
            var token = _token;
            var s = App.Settings.Prop;
            var policy = Networking.NetworkTestResult.Read();
            bool locked = false;
            try
            {
                if (!Networking.NetworkHistory.Accept(snapshot.Timestamp)) return;
                if (snapshot.UniverseId == 0)
                {
                    try { snapshot = snapshot with { UniverseId = await universeResolved.WaitAsync(UniverseLogWait, token) }; }
                    catch (TimeoutException)
                    {
                        using var identityBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
                        identityBudget.CancelAfter(TimeSpan.FromSeconds(8));
                        try { snapshot = snapshot with { UniverseId = await UniverseLookup(snapshot.PlaceId, identityBudget.Token) ?? 0 }; }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                    }
                }
                if (!IsCurrent(snapshot)) return;
                if (_activityWatcher.Data.UniverseId == 0 && snapshot.UniverseId > 0)
                    _activityWatcher.Data.UniverseId = snapshot.UniverseId;
                bool isDeepwoken = snapshot.UniverseId == CompetitiveRegionService.DeepwokenUniverseId;
                // "possible" on purpose: every reserved Deepwoken teleport is a Chime candidate until
                // real session history confirms which Place IDs are actually the Chime destination.
                bool possibleChime = isDeepwoken && snapshot.IsTeleport && snapshot.ServerType == ServerType.Reserved;

                string endpoint = snapshot.LiveEndpoint;

                // ---------- stage 1: location + classify (fast path) ----------
                string location = "";
                string regionSource = "Unknown";

                bool pendingMatches = _pendingJoin?.JobId == snapshot.JobId && _pendingJoin.PlaceId == snapshot.PlaceId;
                int? dcId = pendingMatches ? _pendingJoin!.DataCenterId : null;
                if (pendingMatches && !string.IsNullOrWhiteSpace(_pendingJoin!.Region))
                { location = _pendingJoin.Region; regionSource = "Selected public server metadata"; }
                if (location.Length == 0)
                {
                    try
                    {
                        if (Networking.KnownServerRegions.ForPlace(snapshot.PlaceId).TryGetValue(snapshot.JobId, out var known))
                        { location = known.Region; dcId = known.DataCenterId; regionSource = "Observed server history"; }
                    }
                    catch (Exception ex) { App.Logger.WriteException("NetworkMonitor::ReadRegion", ex); }
                }
                if (location.Length == 0 && !string.IsNullOrEmpty(endpoint))
                {
                    for (int attempt = 0; attempt < 3 && IsCurrent(snapshot); attempt++)
                    {
                        try
                        {
                            (location, regionSource) = await LocationLookup(endpoint, token);
                            if (!string.IsNullOrWhiteSpace(location) && location != "Unknown") break;
                            location = ""; regionSource = "Unknown";
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Location query attempt {attempt + 1} failed: {ex.Message}"); }
                        if (attempt < 2) await Task.Delay(LocationRetryDelay, token);
                    }
                }

                token.ThrowIfCancellationRequested();
                if (!IsCurrent(snapshot)) return;
                if (dcId is > 0)
                {
                    try { await CompetitiveRegionService.EnsureRegistryLoadedAsync().WaitAsync(TimeSpan.FromSeconds(5), token); }
                    catch (TimeoutException) { /* IP location remains usable without the registry. */ }
                    if (CompetitiveRegionService.FindByDataCenterId(dcId.Value) is not null)
                        regionSource = "Roblox DataCenterId";
                }
                var classification = CompetitiveRegionService.Classify(location, dcId, source: regionSource);

                var evt = new CompetitiveNetworkEvent
                {
                    ProcessId = _activityWatcher.RobloxProcessId,
                    Timestamp = snapshot.Timestamp,
                    UniverseId = snapshot.UniverseId,
                    PlaceId = snapshot.PlaceId,
                    JobId = snapshot.JobId,
                    IsTeleport = snapshot.IsTeleport,
                    IsReservedServer = snapshot.ServerType == ServerType.Reserved,
                    MachineAddress = snapshot.MachineAddress,
                    UdmuxAddress = snapshot.UdmuxAddress,
                    UdmuxPort = snapshot.UdmuxPort,
                    RccAddress = snapshot.RccAddress,
                    RccPort = snapshot.RccPort,
                    Location = classification.DisplayName,
                    RegionSource = classification.Source,
                    RegionScore = classification.Score,
                    RegionQuality = classification.Quality,
                    IsPreferredRegion = classification.IsPreferred,
                    IsDeepwoken = isDeepwoken,
                    IsPossibleChime = possibleChime,
                    RunLabel = s.CompetitiveRunLabel
                };

                CompetitiveSessionLogger.Write(
                    $"NET: join Place={snapshot.PlaceId} Job={ShortJob(snapshot.JobId)} endpoint={endpoint}{(snapshot.UdmuxAddress is null ? "" : " (UDMUX)")} region='{classification.DisplayName}' q={classification.Quality} src={regionSource}");

                RecordLatest(evt);

                try { Networking.KnownServerRegions.Observe(evt); }
                catch (Exception ex) { App.Logger.WriteException("NetworkMonitor::RememberRegion", ex); }

                // fast path first: the Chime balloon must not wait for ICMP/traceroute
                if (IsCurrent(snapshot)) Publish(RegionResolved, evt);
                // Warning and leave decisions require only confirmed identity and location.
                // A slow/failed Cloudflare query or queued diagnostics cannot suppress them.
                if (IsCurrent(snapshot) && AutoLogHandler is not null && await AutoLogHandler(evt))
                    return;

                // ---------- stage 2: diagnostics ----------
                await _diagSemaphore.WaitAsync(token);
                locked = true;
                bool doIcmp = s.CompetitiveNetworkMonitorEnabled && s.CompetitiveIcmpEnabled && !string.IsNullOrEmpty(endpoint);
                bool doCf = s.CompetitiveNetworkMonitorEnabled && s.CompetitiveCloudflareDetectionEnabled;
                bool doTrace = s.CompetitiveNetworkMonitorEnabled && s.CompetitiveTracerouteEnabled && !string.IsNullOrEmpty(endpoint);

                // experiment mode forces the full measurement set (normal use already does region + light history)
                if (s.CompetitiveNetworkMonitorEnabled && s.CompetitiveExperimentMode && policy?.IcmpAvailable != false)
                {
                    doIcmp = !string.IsNullOrEmpty(endpoint);
                    doCf = policy?.CloudflareAvailable != false;
                }

                CloudflareTraceResult? cf = null;
                if (doCf)
                {
                    try { cf = await CloudflareNetworkState.QueryAsync(token); }
                    catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Cloudflare query failed: {ex.Message}"); }
                }

                NetworkLatencyMeasurement? latency = null;
                if (doIcmp)
                {
                    try { latency = await _diagnostics.MeasureIcmpAsync(endpoint, 6, 800, token); }
                    catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"ICMP failed: {ex.Message}"); }
                }

                evt = evt with { Cloudflare = cf, Latency = latency };
                await Networking.AdaptiveRegionService.ObserveAsync(evt, token);
                string traceFile = "";
                if (doTrace)
                {
                    try { traceFile = await _diagnostics.RunTracerouteAsync(endpoint, snapshot.JobId, token) ?? ""; }
                    catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Traceroute failed: {ex.Message}"); }
                }
                if (!Networking.NetworkHistory.Accept(snapshot.Timestamp)) return;
                evt = evt with { Cloudflare = cf, Latency = latency, TracerouteFile = traceFile };

                string icmpDisplay = latency is null
                    ? "off"
                    : (latency.HasMeasurement ? $"{latency.AverageMs}ms j{latency.JitterMs}" : "no replies");

                CompetitiveSessionLogger.Write(
                    $"NET: diagnostics done warp={cf?.Warp ?? "n/a"} colo='{cf?.Colo ?? ""}' icmp={icmpDisplay} trace={(traceFile.Length > 0 ? "saved" : "off")}");

                // ---------- persist + stage 2 event ----------
                await CompetitiveSessionLogger.WriteNetworkEventAsync(evt, token);

                RecordLatest(evt);
                if (IsCurrent(snapshot)) Publish(DiagnosticsCompleted, evt);

                App.Logger.WriteLine(LOG_IDENT,
                    $"Evaluated: dw={isDeepwoken} chime?={possibleChime} endpoint={endpoint} region='{evt.Location}' q={classification.Quality} warp={cf?.Warp ?? "n/a"}");
            }
            catch (OperationCanceledException) { /* Roblox exited / app closing */ }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::ProcessAsync", ex);
                CompetitiveSessionLogger.Write($"NET: evaluation failed: {ex.Message}");
            }
            finally
            {
                if (locked) _diagSemaphore.Release();
            }
        }

        private void RecordLatest(CompetitiveNetworkEvent evt)
        {
            lock (_latestLock)
            {
                if (!string.IsNullOrEmpty(evt.JobId))
                    _latestByJob[evt.JobId] = evt;

                if (_latestByJob.Count > 256)
                    _latestByJob.Remove(_latestByJob.Keys.First());
                if (_isDisposed || !_activityWatcher.InGame || _activityWatcher.Data.JobId != evt.JobId || _activeJoinTimestamp != evt.Timestamp) return;
                _lastEvent = evt;
                // Serialize writers together: the temp file must not race between stages/teleports.
                CompetitiveNetworkState.Write(evt);
            }
        }

        private static async Task<(string Location, string Source)> QueryLocationAsync(string endpoint, CancellationToken token)
        {
            var lookup = new ActivityData { MachineAddress = endpoint };
            string location = await lookup.QueryServerLocation(token, showErrors: false) ?? "";
            return (location, location.Length == 0 ? "Unknown" : lookup.LastLocationSource);
        }

        /// <summary>Most recent evaluated event (any job). Null before the first join.</summary>
        public CompetitiveNetworkEvent? LatestEvent
        {
            get { lock (_latestLock) return _lastEvent; }
        }

        /// <summary>Latest evaluated event for a specific job id, if any.</summary>
        public CompetitiveNetworkEvent? GetLatestEvent(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return null;

            lock (_latestLock)
                return _latestByJob.TryGetValue(jobId, out var evt) ? evt : null;
        }

        private static string ShortJob(string jobId) =>
            string.IsNullOrEmpty(jobId) ? "?" : (jobId.Length > 8 ? jobId[..8] + "…" : jobId);

        private void OnGameLeave(object? sender, EventArgs e)
        {
            _seenEvents.Clear(); // A later rejoin of this same job is a new session.
            lock (_latestLock)
            {
                _lastEvent = null;
                _activeJoinTimestamp = default;
                CompetitiveNetworkState.ClearIfOwned(_activityWatcher.RobloxProcessId, _activityWatcher.Data.JobId);
            }
        }

        private bool IsCurrent(ActivitySnapshot snapshot)
        {
            lock (_latestLock) return !_isDisposed && _activityWatcher.InGame &&
                _activityWatcher.Data.JobId == snapshot.JobId && _activeJoinTimestamp == snapshot.Timestamp;
        }

        private void Publish(EventHandler<CompetitiveNetworkEvent>? handlers, CompetitiveNetworkEvent evt)
        {
            if (handlers is null || _isDisposed) return;
            foreach (EventHandler<CompetitiveNetworkEvent> handler in handlers.GetInvocationList())
                try { handler(this, evt); }
                catch (Exception ex) { App.Logger.WriteException(LOG_IDENT, ex); }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { _activityWatcher.OnGameJoin -= OnGameJoin; } catch { /* best effort */ }
            try { _activityWatcher.OnConnectionUpdated -= OnGameJoin; } catch { /* best effort */ }
            try { _activityWatcher.OnGameLeave -= OnGameLeave; } catch { /* best effort */ }
            CompetitiveNetworkState.ClearIfOwned(_activityWatcher.RobloxProcessId);
            _cts.Cancel();
            // In-flight workers own the captured token and release the semaphore during unwind.
            // Leave these managed objects alive until their tasks finish and GC collects the monitor.
            GC.SuppressFinalize(this);
        }
    }
}

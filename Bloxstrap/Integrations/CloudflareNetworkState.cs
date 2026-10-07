using Bloxstrap.Models;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Queries https://www.cloudflare.com/cdn-cgi/trace to learn what Cloudflare actually sees:
    /// WARP tunnel state, ingress POP (colo) and public egress IP.
    ///
    /// Caching rules (never query per log line):
    /// - result is cached with a 30s TTL; the monitor queries at Roblox launch and on each join,
    ///   so in practice this hits the network at most once every 30 seconds.
        /// - failure returns null ("unknown"); stale routing state must not describe a new match.
    /// </summary>
    internal static class CloudflareNetworkState
    {
        private const string LOG_IDENT = "CloudflareNetworkState";
        private const string TraceUrl = "https://www.cloudflare.com/cdn-cgi/trace";

        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        private static readonly HttpClient _client = CreateClient();
        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
            // A pooled pre-switch connection can describe the old route instead of WARP's new state.
            client.DefaultRequestHeaders.ConnectionClose = true;
            return client;
        }

        private static CloudflareTraceResult? _cached;
        private static DateTime _lastQueryUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _queryLock = new(1, 1);

        /// <summary>Current cached state without querying (may be null).</summary>
        public static CloudflareTraceResult? Cached => DateTime.UtcNow - _lastQueryUtc < CacheTtl ? _cached : null;

        public static async Task<CloudflareTraceResult?> QueryAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            bool locked = false;

            try
            {
                await _queryLock.WaitAsync(cancellationToken);
                locked = true;

                if (!forceRefresh && _cached is not null && DateTime.UtcNow - _lastQueryUtc < CacheTtl)
                    return _cached;

                string raw = await _client.GetStringAsync(TraceUrl, cancellationToken);
                var result = Parse(raw);

                _cached = result;
                _lastQueryUtc = DateTime.UtcNow;

                App.Logger.WriteLine(LOG_IDENT, $"trace: warp={result.Warp} colo='{result.Colo}' loc='{result.Location}' ip={result.MaskedPublicIp}");
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { _cached = null; return null; }
            catch (OperationCanceledException) { _cached = null; throw; }
            catch (Exception ex)
            {
                _cached = null;
                App.Logger.WriteLine(LOG_IDENT, $"Cloudflare trace query failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (locked)
                    _queryLock.Release();
            }
        }

        /// <summary>Parses the "key=value" line format of cdn-cgi/trace. Pure - unit testable.</summary>
        public static CloudflareTraceResult Parse(string raw)
        {
            string warp = "unknown", colo = "", ip = "", loc = "", gateway = "";

            foreach (string line in raw.Split('\n'))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;

                string key = line[..idx].Trim().ToLowerInvariant();
                string value = line[(idx + 1)..].Trim();

                switch (key)
                {
                    case "warp": warp = value; break;
                    case "colo": colo = value; break;
                    case "ip": ip = value; break;
                    case "loc": loc = value; break;
                    case "gateway": gateway = value; break;
                }
            }

            return new CloudflareTraceResult
            {
                Warp = warp,
                Colo = colo,
                PublicIp = ip,
                Location = loc,
                Gateway = gateway
            };
        }
    }
}

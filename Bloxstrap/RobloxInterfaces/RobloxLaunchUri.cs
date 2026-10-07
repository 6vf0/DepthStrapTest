using System.Web;

namespace Bloxstrap.RobloxInterfaces
{
    /// <summary>
    /// Parses and rebuilds Roblox launch URIs (roblox://experiences/start?...) without fragile
    /// string replacement. Every field that isn't explicitly modified is preserved.
    /// </summary>
    public sealed class RobloxLaunchUri
    {
        private const string DefaultScheme = "roblox";
        private const string DefaultHost = "experiences";
        private const string DefaultPath = "/start";

        /// <summary>Ordered query parameters (insertion order preserved on rebuild).</summary>
        public readonly List<KeyValuePair<string, string>> Parameters = new();

        public string Scheme { get; }
        public string Host { get; }
        public string Path { get; }

        private RobloxLaunchUri(string scheme, string host, string path)
        {
            Scheme = scheme;
            Host = host;
            Path = path;
        }

        /// <summary>
        /// Parses a launch URI or plain query string. Returns null when it doesn't look like a
        /// Roblox experiences/start URI (e.g. roblox://navigation/home).
        /// </summary>
        public static RobloxLaunchUri? TryParse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string uri = raw.Trim().Trim('"');

            // accept both "roblox://experiences/start?..." and bare "?placeId=..." / "placeId=..."
            string scheme = DefaultScheme, host = DefaultHost, path = DefaultPath;
            string queryPart;

            int qIdx = uri.IndexOf('?');
            if (qIdx == -1)
            {
                if (uri.Contains("://") || !uri.Contains('=')) return null;
                uri = "?" + uri;
                qIdx = 0;
            }

            string head = uri[..qIdx];
            queryPart = uri[(qIdx + 1)..];

            if (!string.IsNullOrEmpty(head))
            {
                int schemeSep = head.IndexOf("://", StringComparison.OrdinalIgnoreCase);
                if (schemeSep > 0)
                {
                    scheme = head[..schemeSep].ToLowerInvariant();
                    string rest = head[(schemeSep + 3)..];

                    int slashIdx = rest.IndexOf('/');
                    host = slashIdx == -1 ? rest : rest[..slashIdx];
                    path = slashIdx == -1 ? "" : rest[slashIdx..];
                }
                else if (head.StartsWith("/", StringComparison.OrdinalIgnoreCase))
                {
                    // relative form: /experiences/start?... handled by caller; treat as host+path
                    int slashIdx = head.IndexOf('/', 1);
                    host = slashIdx == -1 ? "" : head[1..slashIdx];
                    path = slashIdx == -1 ? "/" : head[slashIdx..];
                }
            }

            // only intercept the standard experiences/start surface; leave everything else alone
            if (scheme != DefaultScheme || !host.Equals(DefaultHost, StringComparison.OrdinalIgnoreCase) ||
                !path.Equals(DefaultPath, StringComparison.OrdinalIgnoreCase))
                return null;

            var parsed = new RobloxLaunchUri(scheme, host, path);

            foreach (var pair in queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = HttpUtility.UrlDecode(pair[..eq]);
                string value = HttpUtility.UrlDecode(pair[(eq + 1)..]);
                parsed.Parameters.Add(new KeyValuePair<string, string>(key, value));
            }

            return parsed;
        }

        public string? GetParameter(string name) =>
            Parameters.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

        public bool HasParameter(string name) =>
            Parameters.Any(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Replaces (or appends) a parameter while preserving the others and their order.</summary>
        public void SetParameter(string name, string value)
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    Parameters[i] = new KeyValuePair<string, string>(name, value);
                    return;
                }
            }

            Parameters.Add(new KeyValuePair<string, string>(name, value));
        }

        public long? PlaceId => long.TryParse(GetParameter("placeId"), out var p) ? p : null;
        public string? GameInstanceId => GetParameter("gameInstanceId");
        public string? AccessCode => GetParameter("accessCode");
        public string? LaunchData => GetParameter("launchData");

        /// <summary>True when this URI already pins a concrete server instance.</summary>
        public bool HasExplicitServerId => !string.IsNullOrEmpty(GameInstanceId);

        /// <summary>Rebuilds the full launch URI with all (possibly modified) parameters.</summary>
        public string Build()
        {
            var sb = new StringBuilder();
            sb.Append(Scheme).Append("://").Append(Host).Append(Path);
            if (Parameters.Count > 0) sb.Append('?');

            for (int i = 0; i < Parameters.Count; i++)
            {
                if (i > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(Parameters[i].Key))
                  .Append('=')
                  .Append(Uri.EscapeDataString(Parameters[i].Value));
            }

            return sb.ToString();
        }
    }
}

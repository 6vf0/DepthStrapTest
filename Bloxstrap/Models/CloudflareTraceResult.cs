namespace Bloxstrap.Models
{
    /// <summary>
    /// What Cloudflare actually sees for this machine, from https://www.cloudflare.com/cdn-cgi/trace.
    /// More useful than warp-cli: it reveals the real tunnel state and ingress POP (colo).
    /// The run label is user input and must NOT be trusted as the actual WARP state - record both.
    /// </summary>
    public sealed class CloudflareTraceResult
    {
        /// <summary>"on" / "off" / "unknown".</summary>
        public string Warp { get; init; } = "unknown";

        /// <summary>Ingress POP code, e.g. "DFW", "ORD".</summary>
        public string Colo { get; init; } = "";

        /// <summary>Public egress IP as seen by Cloudflare (diagnostic - treat with privacy care).</summary>
        public string PublicIp { get; init; } = "";

        /// <summary>Cloudflare's location hint, e.g. "TX,US".</summary>
        public string Location { get; init; } = "";

        public string Gateway { get; init; } = "";

        public DateTime QueriedAtUtc { get; init; } = DateTime.UtcNow;

        public bool IsWarpActive => WarpActive is true;

        /// <summary>Unknown must remain distinct from a confirmed direct route.</summary>
        public bool? WarpActive => Warp.ToLowerInvariant() switch
        {
            "on" or "plus" => true,
            "off" => false,
            _ => null
        };

        /// <summary>Privacy-friendly form: 123.45.xxx.xxx (IPv6 keeps first two groups).</summary>
        public string MaskedPublicIp
        {
            get
            {
                if (string.IsNullOrEmpty(PublicIp)) return "";

                var parts = PublicIp.Split(':');
                if (parts.Length == 1) // IPv4
                    return string.Join(".", parts[0].Split('.').Take(2)) + ".xxx.xxx";

                // IPv6: keep the first two hextets
                return string.Join(":", parts.Take(2)) + "::xxxx";
            }
        }
    }
}

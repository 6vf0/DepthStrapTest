using Bloxstrap.Competitive;
using Bloxstrap.Integrations;
using Bloxstrap.Models.Entities;
using Bloxstrap.Roblox;

namespace Bloxstrap.Models
{
    /// <summary>
    /// One fully-evaluated Roblox server join (region + network diagnostics).
    /// Two-stage: identity/region fields are set at stage 1 (fast, ~0.5s after join);
    /// Latency and Cloudflare are filled in at stage 2 when diagnostics finish, then the
    /// JSONL history line is written and NetworkEventDetected fires for UI updates.
    /// </summary>
    public sealed record CompetitiveNetworkEvent
    {
        public int ProcessId { get; init; }
        public DateTime Timestamp { get; init; }

        public long UniverseId { get; init; }

        public long PlaceId { get; init; }

        public string JobId { get; init; } = "";

        public bool IsTeleport { get; init; }

        public bool IsReservedServer { get; init; }

        public string MachineAddress { get; init; } = "";

        public string? UdmuxAddress { get; init; }

        public int? UdmuxPort { get; init; }

        public string? RccAddress { get; init; }

        public int? RccPort { get; init; }

        /// <summary>Resolved display location, e.g. "Dallas, Texas, US". Empty when unknown.</summary>
        public string Location { get; init; } = "";

        /// <summary>"Roblox DataCenterId", "RoValra IP geolocation", "ipinfo.io" or "Unknown".</summary>
        public string RegionSource { get; init; } = "";

        public int RegionScore { get; init; }

        public RegionQuality RegionQuality { get; init; }

        public bool IsPreferredRegion { get; init; }

        /// <summary>Stage 2: filled when ICMP measurement completes (null until then, or forever if disabled/blocked).</summary>
        public NetworkLatencyMeasurement? Latency { get; init; }

        /// <summary>Stage 2: Cloudflare trace state at join time (cached, max once per TTL).</summary>
        public CloudflareTraceResult? Cloudflare { get; init; }

        public bool IsDeepwoken { get; init; }

        /// <summary>Deepwoken + teleport + reserved server. "Possible" until real sessions confirm Chime Place IDs.</summary>
        public bool IsPossibleChime { get; init; }

        /// <summary>User A/B experiment label (Direct / WARP / ...). Never trusted as the actual WARP state.</summary>
        public string RunLabel { get; init; } = "";

        /// <summary>Path of the saved traceroute file, when one was captured. Empty otherwise.</summary>
        public string TracerouteFile { get; init; } = "";
    }
}

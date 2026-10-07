using Bloxstrap.Competitive;
using Bloxstrap.Integrations;

namespace Bloxstrap.Models
{
    /// <summary>
    /// Emitted by CompetitiveRegionMonitor every time a (Deepwoken) server join is evaluated.
    /// Consumed by the tray notification and the session logger/history.
    /// </summary>
    public sealed class CompetitiveRegionResult
    {
        public bool IsDeepwoken { get; init; }

        public bool IsTeleport { get; init; }

        public bool IsReservedServer { get; init; }

        public long UniverseId { get; init; }

        public long PlaceId { get; init; }

        public string JobId { get; init; } = "";

        public string ServerAddress { get; init; } = "";

        /// <summary>Resolved location, e.g. "Dallas, United States" - or empty when unknown.</summary>
        public string Location { get; init; } = "";

        public RegionQuality Quality { get; init; }

        public int Score { get; init; }

        public bool IsPreferred { get; init; }

        /// <summary>"Roblox DataCenterId" | "RoValra IP geolocation" | "ipinfo.io" | "Unknown"</summary>
        public string RegionSource { get; init; } = "Unknown";

        public DateTime Timestamp { get; init; }
    }
}

namespace Bloxstrap.Models
{
    /// <summary>
    /// One persisted competitive session observation (Paths.Cache/CompetitiveRegionHistory.json).
    /// Kept flat and JSON-friendly for later offline analysis of Chime region pulls.
    /// Ping is only stored when we actually measured it - never fabricated from geography.
    /// </summary>
    public sealed class CompetitiveSessionEntry
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("universeId")]
        public long UniverseId { get; set; }

        [JsonPropertyName("placeId")]
        public long PlaceId { get; set; }

        [JsonPropertyName("jobId")]
        public string JobId { get; set; } = "";

        [JsonPropertyName("serverType")]
        public string ServerType { get; set; } = "Public";

        [JsonPropertyName("isDeepwoken")]
        public bool IsDeepwoken { get; set; }

        [JsonPropertyName("isTeleport")]
        public bool IsTeleport { get; set; }

        [JsonPropertyName("machineAddress")]
        public string MachineAddress { get; set; } = "";

        [JsonPropertyName("location")]
        public string Location { get; set; } = "";

        [JsonPropertyName("preferredRegion")]
        public string PreferredRegion { get; set; } = "";

        [JsonPropertyName("quality")]
        public string Quality { get; set; } = "Unknown";

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("regionSource")]
        public string RegionSource { get; set; } = "Unknown";

        /// <summary>Real measured ping in ms, only when a reliable measurement exists. Null otherwise.</summary>
        [JsonPropertyName("pingMs")]
        public int? PingMs { get; set; }
    }
}

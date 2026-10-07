namespace Bloxstrap.Models
{
    /// <summary>
    /// Supplementary ICMP measurement against the live Roblox endpoint.
    /// IMPORTANT: Roblox gameplay is UDP and endpoints may ignore ICMP echo, so
    /// Received == 0 means "no ICMP measurement available" - NEVER "100% gameplay packet loss".
    /// Display as "ICMP RTT", not bare "Ping" (Roblox's in-game Network Ping is the authoritative one).
    /// </summary>
    public sealed class NetworkLatencyMeasurement
    {
        public int Sent { get; init; }

        public int Received { get; init; }

        public double LossPercent { get; init; }

        public double? MinimumMs { get; init; }

        public double? AverageMs { get; init; }

        public double? MaximumMs { get; init; }

        /// <summary>Mean of absolute RTT differences between consecutive successful replies (simple A/B-grade jitter).</summary>
        public double? JitterMs { get; init; }

        public bool HasMeasurement => Received > 0;

        /// <summary>One-line display form, e.g. "ICMP RTT: 31 ms" or "no ICMP measurement".</summary>
        public string ToDisplayString() => HasMeasurement
            ? $"ICMP RTT: {AverageMs?.ToString("0")} ms (jitter {JitterMs?.ToString("0")})"
            : "no ICMP measurement";
    }
}

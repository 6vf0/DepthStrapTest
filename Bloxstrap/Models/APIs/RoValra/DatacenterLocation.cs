namespace Bloxstrap.Models.APIs.RoValra
{
    /// <summary>
    /// RoValra datacenter location. The original v1.5.1 model only used City/Country;
    /// the extra fields are optional (the API provides them) and old cached payloads without
    /// them still deserialize fine.
    /// </summary>
    public class DatacenterLocation
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = "";

        /// <summary>State / province, when the API provides it (e.g. "Texas").</summary>
        [JsonPropertyName("region")]
        public string Region { get; set; } = "";

        /// <summary>Country - ISO code in current payloads ("US"), full name in older ones.</summary>
        [JsonPropertyName("country")]
        public string Country { get; set; } = "";

        /// <summary>Full country name, e.g. "United States".</summary>
        [JsonPropertyName("country_name")]
        public string CountryName { get; set; } = "";

        /// <summary>"[lat, lon]" as strings when the API provides them.</summary>
        [JsonPropertyName("latLong")]
        public List<string> LatLong { get; set; } = new();

        public double? Latitude =>
            LatLong.Count > 0 && double.TryParse(LatLong[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) ? lat : null;

        public double? Longitude =>
            LatLong.Count > 1 && double.TryParse(LatLong[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon) ? lon : null;
    }
}

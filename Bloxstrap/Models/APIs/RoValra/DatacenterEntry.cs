namespace Bloxstrap.Models.APIs.RoValra
{
    public class DatacenterEntry
    {
        [JsonPropertyName("location_id")]
        public int LocationId { get; set; }

        [JsonPropertyName("location")]
        public DatacenterLocation Location { get; set; } = new();

        [JsonPropertyName("dataCenterIds")]
        public List<int> DataCenterIds { get; set; } = new();

        [JsonPropertyName("inactive")]
        public bool Inactive { get; set; }

        [JsonPropertyName("loadbalancing")]
        public bool LoadBalancing { get; set; }
    }
}

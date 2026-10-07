using System.Net;
using System.Net.Sockets;

namespace Bloxstrap.Networking
{
    internal sealed record RoutingTarget(string City, string Country, string Address);
    internal static class RoutingTargetDiscovery
    {
        internal static List<RoutingTarget> Parse(string linksJson, string exchangesJson)
        {
            using var links = JsonDocument.Parse(linksJson);
            using var exchanges = JsonDocument.Parse(exchangesJson);
            var cities = exchanges.RootElement.GetProperty("data").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetInt32(),
                x => (City: x.GetProperty("city").GetString() ?? "", Country: x.GetProperty("country").GetString() ?? ""));
            return links.RootElement.GetProperty("data").EnumerateArray()
                .Where(x => x.GetProperty("status").GetString() == "ok" && x.GetProperty("operational").GetBoolean())
                .SelectMany(x => new[] { "ipaddr4", "ipaddr6" }.Select(field =>
                    (Ix: x.GetProperty("ix_id").GetInt32(), Ip: x.TryGetProperty(field, out var address) && address.ValueKind == JsonValueKind.String ? address.GetString() : null)))
                .Where(x => cities.ContainsKey(x.Ix) && IPAddress.TryParse(x.Ip, out var ip) && !IPAddress.IsLoopback(ip) &&
                    !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) && !ip.IsIPv6LinkLocal)
                .Select(x => new RoutingTarget(cities[x.Ix].City, cities[x.Ix].Country, x.Ip!))
                .Where(x => x.City.Length > 0).DistinctBy(x => x.Address).ToList();
        }
    }
}

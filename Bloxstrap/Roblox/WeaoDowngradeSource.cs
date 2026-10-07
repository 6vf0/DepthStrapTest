using System.Net.Http;

namespace Bloxstrap.Roblox
{
    internal static class WeaoDowngradeSource
    {
        public const string Site = "https://rdd.weao.gg/";
        // This is the endpoint used by RDD's Download Previous action.
        public const string PreviousApi = "https://weao.gg/api/versions/past";
        public static string ParsePrevious(string json)
        {
            using var document = JsonDocument.Parse(json);
            string? version = document.RootElement.GetProperty("Windows").GetString();
            if (version is not null && !version.StartsWith("version-", StringComparison.Ordinal)) version = "version-" + version;
            if (!RobloxVersionArchive.IsVersionId(version)) throw new InvalidDataException("WEAO RDD returned an invalid Windows Player version.");
            return version!.ToLowerInvariant();
        }
        public static async Task<string> GetPreviousAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 64 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WEAO-3PService DepthStrap/1.0.0");
            return ParsePrevious(await client.GetStringAsync(PreviousApi));
        }
        public static string DownloadLink(string? version) => Site + "?binaryType=WindowsPlayer&channel=LIVE" +
            (RobloxVersionArchive.IsVersionId(version) ? "&version=" + Uri.EscapeDataString(version!) : "");
    }
}

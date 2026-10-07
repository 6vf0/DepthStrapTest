namespace Bloxstrap.Networking
{
    /// <summary>Bounded public requests; never attach Roblox authentication.</summary>
    internal static class PublicNetworkHttp
    {
        internal static async Task<string> GetAsync(HttpClient client, string url, CancellationToken token, int retryDelayMs = 500)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var response = await client.GetAsync(url, token);
                    if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < 2)
                    { await Task.Delay(retryDelayMs * (attempt + 1), token); continue; }
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(token);
                }
                catch (HttpRequestException ex) when (attempt < 2 && ex.StatusCode is null)
                { await Task.Delay(retryDelayMs * (attempt + 1), token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < 2)
                { await Task.Delay(retryDelayMs * (attempt + 1), token); }
            }
        }
    }
}

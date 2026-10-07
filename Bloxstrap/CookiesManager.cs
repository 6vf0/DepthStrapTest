namespace Bloxstrap
{
    // Dormant integrations keep this compatibility surface; account access is unavailable.
    // No cookie reads, decryption, credential storage or authenticated requests remain.
    public sealed class CookiesManager
    {
        public bool Loaded => false;
        public CookieState State => CookieState.NotAllowed;
        public Task LoadCookies() => Task.CompletedTask;
        public Task<AuthenticatedUser?> GetAuthenticated() => Task.FromResult<AuthenticatedUser?>(null);
        public Task<HttpResponseMessage> AuthGet(string uri) => Task.FromException<HttpResponseMessage>(new NotSupportedException("DepthStrap does not access Roblox accounts."));
        public Task<HttpResponseMessage> AuthPost(string uri, HttpContent? content) => AuthGet(uri);
    }
}

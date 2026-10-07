namespace Bloxstrap.Roblox
{
    internal static class MultiInstanceLifetime
    {
        public static bool ShouldStop(TimeSpan elapsed, bool seenPlayer, int playerCount, bool launching) =>
            playerCount == 0 && !launching && (seenPlayer || elapsed >= TimeSpan.FromSeconds(20));
    }
}

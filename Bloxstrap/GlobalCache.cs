namespace Bloxstrap
{
    public static class GlobalCache
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> ServerLocation = new();

        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime?> ServerTime = new();
    }
}

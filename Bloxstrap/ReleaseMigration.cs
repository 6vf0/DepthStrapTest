namespace Bloxstrap
{
    internal static class ReleaseMigration
    {
        internal static bool IsPrototypeToFirstRelease(string? product, string? installed, string? incoming) =>
            product == "DepthStrap" && incoming?.StartsWith("1.0.0", StringComparison.Ordinal) == true &&
            (installed?.StartsWith("1.5.1", StringComparison.Ordinal) == true || installed?.StartsWith("1.5.2", StringComparison.Ordinal) == true);
        internal static bool NeedsLegacyMigrations(string? product) => product != "DepthStrap";
    }
}

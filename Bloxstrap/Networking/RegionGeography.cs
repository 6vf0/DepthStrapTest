namespace Bloxstrap.Networking
{
    internal static class RegionGeography
    {
        private static readonly HashSet<string> NorthAmerica = new(StringComparer.OrdinalIgnoreCase)
            { "US", "CA", "MX", "United States", "United States of America", "Canada", "Mexico" };
        private static readonly HashSet<string> Europe = new(StringComparer.OrdinalIgnoreCase)
        {
            "GB", "UK", "IE", "DE", "FR", "NL", "BE", "LU", "AT", "CH", "SE", "NO", "DK", "FI", "IS",
            "ES", "PT", "IT", "GR", "PL", "CZ", "SK", "HU", "RO", "BG", "HR", "SI", "EE", "LV", "LT",
            "MT", "CY", "RS", "BA", "ME", "AL", "MK", "MD", "UA", "BY", "TR", "RU",
            "United Kingdom", "Ireland", "Germany", "France", "Netherlands", "Belgium", "Luxembourg", "Austria",
            "Switzerland", "Sweden", "Norway", "Denmark", "Finland", "Iceland", "Spain", "Portugal", "Italy", "Greece",
            "Poland", "Czechia", "Czech Republic", "Slovakia", "Hungary", "Romania", "Bulgaria", "Croatia", "Slovenia",
            "Estonia", "Latvia", "Lithuania", "Malta", "Cyprus", "Serbia", "Bosnia and Herzegovina", "Montenegro",
            "Albania", "North Macedonia", "Moldova", "Ukraine", "Belarus", "Turkey", "Russia"
        };
        public static bool IsNorthAmerica(string? country) => NorthAmerica.Contains(country?.Trim() ?? "");
        public static bool IsEurope(string? country) => Europe.Contains(country?.Trim() ?? "");
        public static void ApplyDetectedPreference(Settings settings, string country)
        {
            if (!settings.AutomaticRegionalPreference || country.Length != 2) return;
            settings.PreferNorthAmericaOnly = IsNorthAmerica(country);
            settings.PreferEuropeOnly = IsEurope(country);
        }
    }
}

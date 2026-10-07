namespace Bloxstrap.Competitive
{
    internal static class RegionMonitoringPolicy
    {
        internal static bool NeedsWatcher(Settings settings) => settings.CompetitiveModeEnabled &&
            (settings.CompetitiveNetworkMonitorEnabled || settings.ChimeRegionMonitorEnabled ||
             settings.WarnOnBadChimeRegion || settings.AutoLeaveBadChimeRegion);

        internal static bool NeedsAlerts(Settings settings) => settings.CompetitiveModeEnabled &&
            (settings.ChimeRegionMonitorEnabled || settings.WarnOnBadChimeRegion || settings.AutoLeaveBadChimeRegion);

        internal static bool RepairLegacySetup(Settings settings, Networking.NetworkTestResult? test)
        {
            if (settings.RegionMonitoringVersion >= 1) return false;
            if (test?.RegionsAvailable == false && !settings.ChimeRegionMonitorEnabled && !settings.WarnOnBadChimeRegion)
            {
                // Releases through 1.0.2 switched these off after one failed registry request.
                // Live IP lookup is independent of that optional registry.
                settings.ChimeRegionMonitorEnabled = true;
                settings.WarnOnBadChimeRegion = true;
                App.Logger.WriteLine("RegionMonitoring", "Restored alerts disabled by the old network setup policy.");
            }
            settings.RegionMonitoringVersion = 1;
            return true;
        }
    }
}

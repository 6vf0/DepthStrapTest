using System.IO;
using Bloxstrap;
using Bloxstrap.Competitive;
using Bloxstrap.Integrations;
using Bloxstrap.Models;
using Bloxstrap.Models.Persistable;
using Bloxstrap.Networking;
using Bloxstrap.Roblox;
using Bloxstrap.RobloxInterfaces;

internal static class FeatureChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ReleaseMigration.IsPrototypeToFirstRelease("DepthStrap", "1.5.1", "1.0.0") && !ReleaseMigration.NeedsLegacyMigrations("DepthStrap") && ReleaseMigration.NeedsLegacyMigrations("Froststrap"), "DepthStrap 1.0 can replace the prototype without triggering upstream version migrations");
        var settings = new Settings();
        check(settings.PauseRobloxUpdates && settings.AutomaticRegionalPreference && settings.CompetitivePreferredCity.Length == 0 && settings.CompetitiveFallbackCities.Count == 0,
            "Fresh installs pause updates and learn regions without a seeded city");
        new NetworkTestResult { DirectCountry = "GB" }.Apply(settings);
        check(settings.PreferEuropeOnly && !settings.PreferNorthAmericaOnly, "A normal UK connection enables Prefer EU automatically");
        new NetworkTestResult { DirectCountry = "US" }.Apply(settings);
        check(settings.PreferNorthAmericaOnly && !settings.PreferEuropeOnly, "A normal US connection enables Prefer NA automatically");
        new NetworkTestResult { DirectCountry = "SG" }.Apply(settings);
        check(!settings.PreferNorthAmericaOnly && !settings.PreferEuropeOnly, "Players elsewhere are not restricted to NA or EU");
        settings.AutomaticRegionalPreference = false; settings.PreferEuropeOnly = true;
        new NetworkTestResult { DirectCountry = "US" }.Apply(settings);
        check(settings.PreferEuropeOnly, "Follow-up tests preserve a manual regional filter");
        App.Settings.Prop = new Settings { PreferEuropeOnly = true, CompetitiveModeEnabled = true, AdaptiveRegionPreferencesEnabled = true };
        check(CompetitiveRegionService.Classify("London, United Kingdom").Quality == RegionQuality.Acceptable,
            "EU players are not warned about an unlisted EU server");
        check(CompetitiveRegionService.Classify("Toronto, Canada").Quality == RegionQuality.Bad,
            "A known NA server outside the EU filter is classified as bad");
        check(CompetitiveRegionService.Classify("Unknown").Quality == RegionQuality.Unknown,
            "An unresolved server never becomes a bad-region warning");
        App.Settings.Save();
        var now = DateTimeOffset.UtcNow;
        var measured = new[] {
            new RegionObservation { Timestamp = now, City = "London", Country = "GB", Route = "fixture-eu", AverageMs = 25, IsCalibration = true },
            new RegionObservation { Timestamp = now, City = "Paris", Country = "FR", Route = "fixture-eu", AverageMs = 35, IsCalibration = true },
            new RegionObservation { Timestamp = now, City = "Toronto", Country = "CA", Route = "fixture-eu", AverageMs = 5, IsCalibration = true },
            new RegionObservation { Timestamp = now, City = "Wrong Route", Country = "GB", Route = "fixture-other", AverageMs = 1, IsCalibration = true }
        };
        AdaptiveRegionService.RecordAsync(measured, "fixture-eu", CancellationToken.None).GetAwaiter().GetResult();
        check(App.Settings.Prop.CompetitivePreferredCity == "London" && App.Settings.Prop.CompetitiveFallbackCities.SequenceEqual(new[] { "Paris" }),
            "Installation measurements populate preferred and fallback cities for the final route and selected area");
        App.Settings.Prop = new Settings { PreferNorthAmericaOnly = true };
        App.Settings.Save();
        AdaptiveRegionService.RecordAsync(new[] {
            new RegionObservation { City = "New York", Country = "US", Route = "direct", AverageMs = 65, IsCalibration = true },
            new RegionObservation { City = "Los Angeles", Country = "US", Route = "direct", AverageMs = 80, IsCalibration = true }
        }, "direct", CancellationToken.None).GetAwaiter().GetResult();
        for (int i = 0; i < 3; i++) AdaptiveRegionService.ObserveAsync(new CompetitiveNetworkEvent {
            Timestamp = DateTime.Now, Location = "Santiago de Querétaro, Mexico", Cloudflare = new CloudflareTraceResult { Warp = "off" },
            Latency = new NetworkLatencyMeasurement { Sent = 6, Received = 6, AverageMs = 25, JitterMs = 2 }
        }, CancellationToken.None).GetAwaiter().GetResult();
        check(App.Settings.Prop.CompetitivePreferredCity == "Santiago de Querétaro" && App.Settings.Prop.CompetitiveFallbackCities.Contains("New York"),
            "A faster Mexico region learned from joins outranks New York and California under Prefer NA");
        var links = new { data = Enumerable.Range(1, 30).Select(i => new { ix_id = i, status = "ok", operational = true, ipaddr4 = "192.0.2." + i }) };
        var exchanges = new { data = Enumerable.Range(1, 30).Select(i => new { id = i, city = i == 30 ? "Santiago de Querétaro" : "Fixture " + i, country = i == 30 ? "MX" : "US" }) };
        var discovered = RoutingTargetDiscovery.Parse(System.Text.Json.JsonSerializer.Serialize(links), System.Text.Json.JsonSerializer.Serialize(exchanges));
        check(discovered.Count == 30 && discovered.Any(x => x.Country == "MX"), "Discovery includes all published locations, including Mexico beyond the old 24-city cutoff");
        var expandedLinks = new { data = Enumerable.Range(1, 5).Select(i => new { ix_id = 1, status = "ok", operational = true, ipaddr4 = "192.0.2." + i, ipaddr6 = "2001:db8::" + i }) };
        discovered = RoutingTargetDiscovery.Parse(System.Text.Json.JsonSerializer.Serialize(expandedLinks), System.Text.Json.JsonSerializer.Serialize(exchanges));
        check(discovered.Count == 10 && discovered.Count(x => x.Address.Contains(':')) == 5,
            "Every published interface in the same city remains eligible, including IPv6 beyond the old two-address cap");
        const string installed = "version-0123456789abcdef";
        settings = new Settings();
        check(RobloxUpdatePolicy.RequestedVersion(true, null, settings, installed, true) == installed, "Paused updates retain the installed Player");
        check(RobloxUpdatePolicy.RequestedVersion(true, null, settings, installed, false) is null, "A fresh install still fetches a current Player");
        settings.PauseRobloxUpdates = false;
        check(RobloxUpdatePolicy.RequestedVersion(true, null, settings, installed, true) is null, "Unpausing allows the latest version check");
        settings.PauseRobloxUpdates = true;
        check(RobloxUpdatePolicy.RequestedVersion(false, null, settings, installed, true) is null, "Player update pausing does not pin Studio");
        check(WeaoDowngradeSource.ParsePrevious("{\"Windows\":\"0123456789abcdef\"}") == installed && WeaoDowngradeSource.DownloadLink(installed).Contains("rdd.weao.gg/?binaryType=WindowsPlayer&channel=LIVE&version="),
            "RDD previous versions are normalized and linked to the selected Windows Player build");
        bool invalid = false;
        try { WeaoDowngradeSource.ParsePrevious("{\"Windows\":\"../malicious\"}"); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Invalid downgrade catalog values cannot become deployment paths");
        check(!MultiInstanceLifetime.ShouldStop(TimeSpan.FromSeconds(5), false, 0, false) &&
              !MultiInstanceLifetime.ShouldStop(TimeSpan.FromSeconds(30), false, 0, true) &&
              !MultiInstanceLifetime.ShouldStop(TimeSpan.FromSeconds(30), true, 2, false) &&
              MultiInstanceLifetime.ShouldStop(TimeSpan.FromSeconds(30), true, 0, false),
            "The multi-client helper waits for startup, stays through launches and multiple players, and exits after the last player");
        string name = "DepthStrap-Fixture-" + Guid.NewGuid().ToString("N");
        using (var owner = MultiInstanceWatcher.Acquire(name))
        {
            bool rejected = Task.Run(() => { try { using var second = MultiInstanceWatcher.Acquire(name); second.ReleaseMutex(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
            check(rejected, "The helper refuses a mutex owned by another launch thread");
            owner.ReleaseMutex();
        }
        using (var incompatible = new EventWaitHandle(false, EventResetMode.ManualReset, name))
        {
            bool rejected = false;
            try { using var mutex = MultiInstanceWatcher.Acquire(name); mutex.ReleaseMutex(); } catch (WaitHandleCannotBeOpenedException) { rejected = true; }
            check(rejected, "A preexisting Roblox-style event collision fails instead of claiming multi-client readiness");
        }
        App.Settings.Prop.WarnOnBadChimeRegion = false; App.Settings.Prop.LogCompetitiveSessions = true;
        var bad = new CompetitiveRegionResult { Timestamp = DateTime.Now, IsDeepwoken = true, JobId = "bad-fixture", Location = "Toronto, Canada", Quality = RegionQuality.Bad };
        CompetitiveSessionLogger.WriteBadRegionAsync(bad).GetAwaiter().GetResult();
        check(File.ReadAllLines(CompetitiveSessionLogger.GetJsonlPath()).Any(x => x.Contains("bad_region") && x.Contains("bad-fixture")),
            "Bad-region joins automatically log even when popups are turned off");
        check(CompetitiveRegionMonitor.BuildNotification(bad) is not null && CompetitiveRegionMonitor.BuildNotification(new CompetitiveRegionResult { IsDeepwoken = true, Quality = RegionQuality.Unknown }) is null,
            "Known bad joins generate warnings while unknown initial joins do not");
        var autolog = new Settings { CompetitiveModeEnabled = true, AutoLeaveBadChimeRegion = true, PreferNorthAmericaOnly = true,
            CompetitivePreferredCity = "Dallas", CompetitiveFallbackCities = new() { "Santiago de Querétaro" } };
        var joined = new CompetitiveNetworkEvent { Timestamp = DateTime.Now, UniverseId = CompetitiveRegionService.DeepwokenUniverseId,
            PlaceId = 999001, IsDeepwoken = true, JobId = "current", RegionSource = "Roblox DataCenterId", Location = "London, United Kingdom" };
        var outside = new RegionClassification { Quality = RegionQuality.Bad };
        check(BadRegionAutoLog.ShouldLeave(joined, autolog, "current", DateTime.Now, outside), "A confirmed NA-to-EU Deepwoken join qualifies for opt-in autolog");
        autolog.PreferNorthAmericaOnly = false; autolog.PreferEuropeOnly = true;
        check(BadRegionAutoLog.ShouldLeave(joined with { Location = "Singapore" }, autolog, "current", DateTime.Now, outside), "An EU-to-Asia Deepwoken join qualifies for autolog");
        check(!BadRegionAutoLog.ShouldLeave(joined, autolog, "current", DateTime.Now, new RegionClassification { Quality = RegionQuality.Good, IsConfiguredRegion = true }),
            "Primary and fallback regions are retained");
        check(!BadRegionAutoLog.ShouldLeave(joined, autolog, "current", DateTime.Now, new RegionClassification { Quality = RegionQuality.Unknown }) &&
              !BadRegionAutoLog.ShouldLeave(joined with { RegionSource = "Unknown" }, autolog, "current", DateTime.Now, outside), "Unknown locations never trigger autolog");
        check(!BadRegionAutoLog.ShouldLeave(joined, autolog, "new-job", DateTime.Now, outside) &&
              !BadRegionAutoLog.ShouldLeave(joined with { Timestamp = DateTime.Now.AddMinutes(-2) }, autolog, "current", DateTime.Now, outside), "Old jobs and stale diagnostics cannot close the current session");
        check(!BadRegionAutoLog.ShouldLeave(joined with { IsDeepwoken = false, UniverseId = 123 }, autolog, "current", DateTime.Now, outside) &&
              !BadRegionAutoLog.ShouldRejoin(joined with { UniverseId = 123 }, autolog), "Another game's bad server can neither autolog nor launch Deepwoken");
        check(new long[] { 999002, 999003, 999004, 999005 }.All(id =>
                BadRegionAutoLog.ShouldLeave(joined with { PlaceId = id, IsTeleport = true, IsReservedServer = true }, autolog, "current", DateTime.Now, outside) &&
                BadRegionAutoLog.ShouldRejoin(joined with { PlaceId = id }, autolog)), "Reserved Deepwoken subplaces are recognized by universe without maintaining a list of Layer/Chime place IDs");
        autolog.AutoLeaveBadChimeRegion = false;
        check(!BadRegionAutoLog.ShouldRejoin(joined, autolog), "The single autolog toggle also disables Deepwoken rejoin");
        check(!BadRegionAutoLog.ShouldLeave(joined, autolog, "current", DateTime.Now, outside), "Autolog is opt-in and respects the disabled setting");
        var homeLaunch = AutoLogHomeHandoff.Launch(BadRegionAutoLog.HomeUri, installed, Guid.NewGuid().ToString("N"));
        var parsed = new LaunchSettings(homeLaunch.ArgumentList.ToArray());
        check(parsed.RobloxLaunchArgs == "roblox://navigation/home" && parsed.RobloxLaunchMode == Bloxstrap.Enums.LaunchMode.Player && parsed.VersionFlag.Data == installed,
            "Home handoff uses the native Home URI and exact installed Player build, avoiding an update during multi-client handoff");
        var rejoinLaunch = AutoLogHomeHandoff.Launch(BadRegionAutoLog.RejoinUri, installed);
        check(RobloxLaunchUri.TryParse(rejoinLaunch.ArgumentList[0])?.PlaceId == 4111023553 && !rejoinLaunch.ArgumentList[0].Contains("gameInstanceId"),
            "Rejoin returns to Deepwoken entry matchmaking rather than a reserved subplace or the same bad server");
        check(BadRegionAutoLog.IsIdleHomeLog("[FLog::Output] Home loaded") &&
              !BadRegionAutoLog.IsIdleHomeLog("[FLog::Output] ! Joining game 'fixture' place 123 at 192.0.2.1") &&
              !BadRegionAutoLog.IsIdleHomeLog("[FLog::GameJoinUtil] GameJoinUtil::initiateTeleportToReservedServer"),
            "A new public or reserved game join on Home cancels the queued Deepwoken rejoin");
        check(!AutoLogHomeHandoff.RetryAllowed(Enumerable.Repeat(DateTime.Now, 3), DateTime.Now) &&
              AutoLogHomeHandoff.RetryAllowed(Enumerable.Repeat(DateTime.Now.AddMinutes(-11), 3), DateTime.Now), "Three retries per ten minutes prevent endless autolog/rejoin loops");
        App.Settings.Prop = new Settings { CompetitivePreferredCity = "Old UI choice", PreferNorthAmericaOnly = true };
        var learnedDisk = new JsonManager<Settings>();
        learnedDisk.Prop = new Settings { CompetitivePreferredCity = "Santiago de Querétaro", CompetitiveFallbackCities = new() { "Dallas" }, PreferNorthAmericaOnly = true };
        learnedDisk.Save();
        AdaptiveRegionService.SaveUserSettings();
        check(App.Settings.Prop.CompetitivePreferredCity == "Santiago de Querétaro" && App.Settings.Prop.CompetitiveFallbackCities.SequenceEqual(new[] { "Dallas" }),
            "Saving a stale settings window preserves newly learned cities from the watcher");
        App.Settings.Prop.AdaptiveRegionPreferencesEnabled = false; App.Settings.Prop.CompetitivePreferredCity = "Manual city";
        AdaptiveRegionService.SaveUserSettings(); learnedDisk.Load(false);
        check(learnedDisk.Prop.CompetitivePreferredCity == "Manual city", "Turning off learning allows a manual preference to be saved");
        App.Settings.Prop = new Settings();
    }
}

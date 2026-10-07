using Bloxstrap.Integrations;

namespace Bloxstrap.Networking
{
    internal sealed record ComparisonRun(RouteDecision? Decision, List<RoutingSample> Samples, CloudflareTraceResult? FinalState, string Error, string DirectCountry = "");
    internal static class RouteComparisonRunner
    {
        internal static async Task<CloudflareTraceResult> SwitchAsync(IWarpClient client, bool connected,
            Func<CancellationToken, Task<CloudflareTraceResult?>> state, CancellationToken token, int settlingMs = NetworkCalibrationProfile.SettlingMs, int pollingMs = 1000)
        {
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(token);
            transition.CancelAfter(TimeSpan.FromSeconds(NetworkCalibrationProfile.TransitionSeconds));
            token = transition.Token;
            await client.SetConnectedAsync(connected, token);
            int confirmed = 0;
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(pollingMs, token);
                var actual = await state(token);
                confirmed = actual?.WarpActive == connected ? confirmed + 1 : 0;
                if (confirmed >= 3) { await Task.Delay(settlingMs, token); return actual!; }
            }
            throw new IOException("WARP route change could not be verified. The comparison was stopped.");
        }
        public static async Task<ComparisonRun> RunAsync(IWarpClient client, bool original,
            Func<CancellationToken, Task<CloudflareTraceResult?>> state,
            Func<string, CancellationToken, Task<List<RoutingSample>>> probe,
            IProgress<string>? progress, CancellationToken token, int settlingMs = NetworkCalibrationProfile.SettlingMs, int pollingMs = 1000)
        {
            var samples = new List<RoutingSample>();
            RouteDecision? decision = null;
            CloudflareTraceResult? final = null;
            string error = "", directCountry = "";
            bool selected = false;
            try
            {
                for (int pass = 1; pass <= NetworkCalibrationProfile.PassesPerRoute; pass++)
                {
                    progress?.Report($"Testing normal routing, pass {pass} of {NetworkCalibrationProfile.PassesPerRoute}…");
                    var direct = await SwitchAsync(client, false, state, token, settlingMs, pollingMs);
                    directCountry = direct.Location;
                    samples.AddRange(await probe(AdaptiveRegionService.RouteKey(direct), token));
                    progress?.Report($"Testing Cloudflare WARP, pass {pass} of {NetworkCalibrationProfile.PassesPerRoute}…");
                    var tunneled = await SwitchAsync(client, true, state, token, settlingMs, pollingMs);
                    samples.AddRange(await probe(AdaptiveRegionService.RouteKey(tunneled), token));
                }
                // The observed Roblox gameplay endpoints are IPv4. IPv6 coverage must not
                // select a tunnel on the strength of a different address family alone.
                var gameplayFamily = samples.Where(x => IPAddress.TryParse(x.Address, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                decision = NetworkComparison.Decide(gameplayFamily.Where(x => x.Route == "direct"), gameplayFamily.Where(x => x.Route.StartsWith("warp:", StringComparison.Ordinal)));
                bool target = decision.UseWarp ?? original;
                progress?.Report("Applying and verifying the selected route…");
                final = await SwitchAsync(client, target, state, token, settlingMs, pollingMs);
                selected = true;
            }
            catch (Exception ex)
            {
                error = ex is OperationCanceledException ? "Network comparison was cancelled or timed out." : ex.Message;
                App.Logger.WriteException("NetworkComparison", ex);
            }
            finally
            {
                if (!selected)
                {
                    progress?.Report("Restoring the starting WARP state…");
                    // Restoration has its own budget even after cancellation of the comparison.
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(NetworkCalibrationProfile.TransitionSeconds + 30));
                    try { final = await SwitchAsync(client, original, state, restore.Token, settlingMs, pollingMs); }
                    catch (Exception ex) { final = null; error += " Starting route could not be restored automatically. Open Cloudflare WARP to check your connection. " + ex.Message; }
                }
            }
            return new(decision, samples, final, error, directCountry);
        }
    }
}

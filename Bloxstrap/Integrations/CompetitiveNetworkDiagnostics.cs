using Bloxstrap.Models;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Network measurement code, kept out of ActivityWatcher on purpose:
    /// - ICMP RTT against the live endpoint (supplementary only - UDP gameplay may ignore echo)
    /// - optional traceroute via tracert.exe (background-only; saves to Logs/CompetitiveRoutes/)
    /// Every method fails soft: diagnostics must never block or break Roblox.
    /// </summary>
    internal sealed class CompetitiveNetworkDiagnostics
    {
        private const string LOG_IDENT = "CompetitiveNetworkDiagnostics";

        #region ICMP

        /// <summary>
        /// Simple ICMP measurement: count probes, ~800ms timeout each, 125ms apart.
        /// Jitter = mean of absolute RTT differences between consecutive successful replies
        /// (same simple calculation as RobloxRouteLab.ps1 - enough for A/B routing tests).
        /// Returns a measurement with Received == 0 when the endpoint ignores ICMP.
        /// </summary>
        public async Task<NetworkLatencyMeasurement> MeasureIcmpAsync(
            string host, int count = 6, int timeoutMs = 800, CancellationToken cancellationToken = default, int spacingMs = 125)
        {
            var times = new List<double>();
            int attempted = 0;

            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();

                for (int i = 0; i < count && !cancellationToken.IsCancellationRequested; i++)
                {
                    attempted += 1;
                    try
                    {
                        var reply = await ping.SendPingAsync(host, timeoutMs);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                            times.Add(reply.RoundtripTime);
                    }
                    catch
                    {
                        // per-probe failures just count as loss
                    }

                    if (i < count - 1)
                        await Task.Delay(spacingMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) { /* fall through with what we have */ }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"ICMP measurement failed for {host}: {ex.Message}");
            }

            int sent = attempted;
            double lossPct = sent > 0 ? Round1((1.0 - (times.Count / (double)sent)) * 100.0) : 0;

            if (times.Count == 0)
                return new NetworkLatencyMeasurement { Sent = sent, Received = 0, LossPercent = 100.0 };

            double min = times.Min();
            double max = times.Max();
            double avg = times.Average();

            double jitter = 0;
            if (times.Count > 1)
            {
                double sum = 0;
                for (int i = 1; i < times.Count; i++)
                    sum += Math.Abs(times[i] - times[i - 1]);
                jitter = sum / (times.Count - 1);
            }

            return new NetworkLatencyMeasurement
            {
                Sent = sent,
                Received = times.Count,
                LossPercent = lossPct,
                MinimumMs = Round1(min),
                AverageMs = Round1(avg),
                MaximumMs = Round1(max),
                JitterMs = Round1(jitter)
            };
        }

        private static double Round1(double v) => Math.Round(v, 1);

        #endregion

        #region Traceroute

        /// <summary>
        /// Runs Windows tracert in the background and saves the output.
        /// File: Logs/CompetitiveRoutes/YYYY-MM-DD_HHMMSS_<job8>_<ip>.txt
        /// Returns the saved file path, or null on failure (failure is logged, never thrown).
        /// </summary>
        public async Task<string?> RunTracerouteAsync(string ip, string jobId, CancellationToken cancellationToken = default)
        {
            var startedAt = DateTimeOffset.UtcNow;
            const int OverallCapMs = 45000; // 15 hops x 350ms worst case + margin
            if (!IPAddress.TryParse(ip, out var address)) return null;
            ip = address.ToString();

            try
            {
                string dir = Path.Combine(Paths.Logs, "CompetitiveRoutes");
                Directory.CreateDirectory(dir);

                string safeJob = Sanitize(jobId.Length > 8 ? jobId[..8] : jobId);
                string path = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{safeJob}_{ip}.txt");

                var psi = new ProcessStartInfo("tracert.exe", $"-d -h 15 -w 350 {ip}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "tracert failed to start");
                    return null;
                }

                Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
                Task<string> errTask = proc.StandardError.ReadToEndAsync();

                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(OverallCapMs);
                try
                {
                    await proc.WaitForExitAsync(budget.Token);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    App.Logger.WriteLine(LOG_IDENT, $"tracert to {ip} exceeded {OverallCapMs / 1000}s cap, killed");
                    cancellationToken.ThrowIfCancellationRequested();
                }

                string output = (await outTask) + (await errTask);
                using var resetLock = Networking.NetworkHistory.Lock();
                if (!Networking.NetworkHistory.Accept(startedAt)) return null;
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, output);

                App.Logger.WriteLine(LOG_IDENT, $"Traceroute saved: {path}");
                return path;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"tracert to {ip} failed: {ex.Message}");
                return null;
            }
        }

        private static string Sanitize(string text) =>
            new(text.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

        #endregion
    }
}

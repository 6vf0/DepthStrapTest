namespace Bloxstrap.Competitive
{
    internal static class AutoLogHomeHandoff
    {
        internal sealed record HomeClient(WatcherData Watcher, DateTime StartedAt);
        private static string FilePath(string nonce)
        {
            if (!Guid.TryParseExact(nonce, "N", out _)) throw new ArgumentException("Invalid Home handoff identifier.");
            return Path.Combine(Paths.Cache, "AutoLogHome-" + nonce + ".json");
        }

        internal static void Publish(string? nonce, WatcherData watcher)
        {
            if (string.IsNullOrEmpty(nonce)) return;
            try
            {
                using var player = Process.GetProcessById(watcher.ProcessId);
                Directory.CreateDirectory(Paths.Cache);
                string path = FilePath(nonce);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new HomeClient(watcher, player.StartTime)));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception ex) { App.Logger.WriteException("AutoLog::HomeHandoff", ex); }
        }

        internal static async Task<HomeClient> WaitAsync(string nonce, CancellationToken token)
        {
            string path = FilePath(nonce);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                while (!File.Exists(path)) await Task.Delay(200, timeout.Token);
                return JsonSerializer.Deserialize<HomeClient>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing Home client.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        internal static ProcessStartInfo Launch(string uri, string version, string? nonce = null)
        {
            if (!Roblox.RobloxVersionArchive.IsVersionId(version)) throw new InvalidDataException("Missing current Player version.");
            if (uri != BadRegionAutoLog.HomeUri && uri != BadRegionAutoLog.RejoinUri) throw new ArgumentException("Invalid autolog destination.");
            var info = new ProcessStartInfo(Paths.Application) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add(uri);
            info.ArgumentList.Add("-player");
            // Retain this exact installed build so a handoff cannot upgrade/close other clients.
            info.ArgumentList.Add("-version"); info.ArgumentList.Add(version);
            info.ArgumentList.Add("-quiet");
            info.ArgumentList.Add("-autologhome");
            if (nonce is not null) { _ = FilePath(nonce); info.ArgumentList.Add(nonce); }
            return info;
        }

        internal static bool RetryAllowed(IEnumerable<DateTime> attempts, DateTime now) =>
            attempts.Count(x => x > now.AddMinutes(-10)) < 3;

        internal static bool ReserveRetry()
        {
            using var gate = Networking.NetworkHistory.DataLock("AutoLogRetry", TimeSpan.FromSeconds(2));
            if (!gate.IsAcquired) return false;
            try
            {
                string path = Path.Combine(Paths.Cache, "AutoLogRetries.json");
                var attempts = File.Exists(path) ? JsonSerializer.Deserialize<List<DateTime>>(File.ReadAllText(path)) ?? new() : new List<DateTime>();
                var now = DateTime.Now;
                if (!RetryAllowed(attempts, now)) return false;
                attempts.RemoveAll(x => x <= now.AddMinutes(-10)); attempts.Add(now);
                Directory.CreateDirectory(Paths.Cache);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(attempts)); File.Move(path + ".tmp", path, true);
                return true;
            }
            catch (Exception ex) { App.Logger.WriteException("AutoLog::RetryLimit", ex); return false; }
        }
    }
}

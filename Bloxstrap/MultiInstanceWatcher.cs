namespace Bloxstrap
{
    internal static class MultiInstanceWatcher
    {
        internal const string ReadyEventName = "DepthStrap-MultiInstanceReady";
        public static bool IsReady()
        {
            try { using var ready = EventWaitHandle.OpenExisting(ReadyEventName); return ready.WaitOne(0); }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        internal static Mutex Acquire(string name)
        {
            var mutex = new Mutex(false, name);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return mutex;
            mutex.Dispose();
            throw new InvalidOperationException("Close all Roblox clients before enabling multi-client launching.");
        }

        public static void Run()
        {
            // Ownership stays on this thread for the lifetime of every launched Player.
            Mutex? singleton = null, singletonEvent = null;
            using var guard = new Mutex(false, "DepthStrap-MultiInstanceWatcher");
            bool ownsGuard;
            try { ownsGuard = guard.WaitOne(0); }
            catch (AbandonedMutexException) { ownsGuard = true; }
            if (!ownsGuard) return;
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
            try
            {
                singleton = Acquire("ROBLOX_singletonMutex");
                singletonEvent = Acquire("ROBLOX_singletonEvent");
                ready.Set();
                var elapsed = Stopwatch.StartNew(); bool seenPlayer = false;
                while (true)
                {
                    int count = -1;
                    try
                    {
                        var players = Process.GetProcessesByName("RobloxPlayerBeta");
                        count = players.Length;
                        foreach (var player in players) player.Dispose();
                    }
                    catch (Exception ex) { App.Logger.WriteException("MultiInstanceWatcher", ex); }
                    seenPlayer |= count > 0;
                    if (Roblox.MultiInstanceLifetime.ShouldStop(elapsed.Elapsed, seenPlayer, count,
                        Utilities.DoesMutexExist("Bloxstrap-Bootstrapper"))) break;
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex) { App.Logger.WriteException("MultiInstanceWatcher", ex); }
            finally
            {
                ready.Reset();
                if (singletonEvent is not null) { singletonEvent.ReleaseMutex(); singletonEvent.Dispose(); }
                if (singleton is not null) { singleton.ReleaseMutex(); singleton.Dispose(); }
                guard.ReleaseMutex();
            }
        }
    }
}

using System.Windows;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public void RefreshUpdatePolicy() { OnPropertyChanged(nameof(PauseRobloxUpdates)); OnPropertyChanged(nameof(BackgroundUpdates)); }

        public bool MultiInstances
        {
            get => App.Settings.Prop.MultiInstanceLaunching;
            set
            {
                if (value && !MultiInstanceWatcher.IsReady() && Process.GetProcessesByName("RobloxPlayerBeta").Any())
                {
                    Frontend.ShowMessageBox("Close all Roblox clients before enabling multi-client launching.", MessageBoxImage.Information);
                    OnPropertyChanged(nameof(MultiInstances));
                    return;
                }
                App.Settings.Prop.MultiInstanceLaunching = value;
                OnPropertyChanged(nameof(MultiInstances));
            }
        }
        public bool PauseRobloxUpdates
        {
            get => App.Settings.Prop.PauseRobloxUpdates;
            set { App.Settings.Prop.PauseRobloxUpdates = value; App.Settings.Prop.UpdateRoblox = true; if (value) App.Settings.Prop.BackgroundUpdatesEnabled = false; OnPropertyChanged(nameof(PauseRobloxUpdates)); OnPropertyChanged(nameof(BackgroundUpdates)); }
        }
        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set { App.Settings.Prop.BackgroundUpdatesEnabled = value; if (value) PauseRobloxUpdates = false; }
        }

        public bool CloseCrashHandler
        {
            get => App.Settings.Prop.AutoCloseCrashHandler;
            set => App.Settings.Prop.AutoCloseCrashHandler = value;
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        public CleanerOptions SelectedCleanUpMode
        {
            get => App.Settings.Prop.CleanerOptions;
            set => App.Settings.Prop.CleanerOptions = value;
        }

        public IEnumerable<CleanerOptions> CleanerOptions { get; } = CleanerOptionsEx.Selections;

        public CleanerOptions CleanerOption
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
            }
        }

        private List<string> CleanerItems = App.Settings.Prop.CleanerDirectories;

        public bool CleanerLogs
        {
            get => CleanerItems.Contains("RobloxLogs");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxLogs");
                else
                    CleanerItems.Remove("RobloxLogs");
            }
        }

        public bool CleanerCache
        {
            get => CleanerItems.Contains("RobloxCache");
            set
            {
                if (value)
                    CleanerItems.Add("RobloxCache");
                else
                    CleanerItems.Remove("RobloxCache");
            }
        }

        public bool CleanerFroststrap
        {
            get => CleanerItems.Contains("FroststrapLogs");
            set
            {
                if (value)
                    CleanerItems.Add("FroststrapLogs");
                else
                    CleanerItems.Remove("FroststrapLogs");
            }
        }
    }
}

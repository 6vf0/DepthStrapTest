using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Bloxstrap.Roblox;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public sealed class RobloxVersionArchiveViewModel : NotifyPropertyChangedViewModel
    {
        public Action? UpdatePolicyChanged { get; set; }
        public ObservableCollection<string> CachedVersions { get; } = new(RobloxVersionArchive.InstalledPlayerVersions());
        private string _version = App.Settings.Prop.RobloxPlayerVersionOverride;
        private string _status = "Latest Roblox release selected.";
        public string VersionId { get => _version; set { _version = value ?? ""; OnPropertyChanged(nameof(VersionId)); } }
        public string? SelectedCachedVersion { get => CachedVersions.FirstOrDefault(x => x == VersionId); set { if (value is not null) VersionId = value; } }
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }
        public ICommand OpenDowngradeSourceCommand => new RelayCommand(() => Utilities.ShellExecute(WeaoDowngradeSource.DownloadLink(VersionId)));
        public ICommand DowngradePreviousCommand => new AsyncRelayCommand(DowngradePreviousAsync);
        private async Task DowngradePreviousAsync()
        {
            Status = "Getting the previous Windows Player build from WEAO RDD…";
            try { VersionId = await WeaoDowngradeSource.GetPreviousAsync(); Install(); }
            catch (Exception ex) { Status = "WEAO RDD downgrade lookup failed: " + ex.Message + " Open the source site to select a version manually."; }
        }
        public ICommand InstallVersionCommand => new RelayCommand(Install);
        public ICommand UseLatestCommand => new RelayCommand(() =>
        {
            VersionId = "";
            App.Settings.Prop.RobloxPlayerVersionOverride = "";
            App.Settings.Prop.PauseRobloxUpdates = false;
            App.Settings.Prop.UpdateRoblox = true;
            App.Settings.Save();
            Status = "Latest Roblox release selected. Launch Roblox to install it.";
            UpdatePolicyChanged?.Invoke();
        });
        public ICommand RefreshVersionsCommand => new RelayCommand(() =>
        {
            CachedVersions.Clear();
            foreach (var version in RobloxVersionArchive.InstalledPlayerVersions()) CachedVersions.Add(version);
        });
        public RobloxVersionArchiveViewModel()
        {
            if (App.Settings.Prop.PauseRobloxUpdates) Status = "Roblox updates paused. The installed Player version is retained; the latest is installed when no build exists.";
            if (VersionId.Length > 0) Status = "Pinned to " + VersionId;
        }
        private void Install()
        {
            string version = VersionId.Trim();
            if (!RobloxVersionArchive.IsVersionId(version))
            {
                Status = "Enter version- followed by exactly 16 hexadecimal characters.";
                return;
            }
            // Installation updates shared files and cannot run concurrently with the client.
            if (Process.GetProcessesByName("RobloxPlayerBeta").Any())
            {
                Status = "Close Roblox before installing another version.";
                return;
            }
            try
            {
                App.Settings.Prop.RobloxPlayerVersionOverride = version.ToLowerInvariant();
                App.Settings.Prop.PauseRobloxUpdates = true;
                UpdatePolicyChanged?.Invoke();
                App.Settings.Prop.StaticDirectory = false; // retained version directories are required for rollback
                App.Settings.Save();
                Process.Start(new ProcessStartInfo(Paths.Application, "-player -force") { UseShellExecute = false });
                Status = "Installing " + version + ". Downgrade source: rdd.weao.gg (WEAO RDD). Packages are downloaded from Roblox and verified against its manifest.";
            }
            catch (Exception ex) { Status = "Could not start installation: " + ex.Message; }
        }
    }
}

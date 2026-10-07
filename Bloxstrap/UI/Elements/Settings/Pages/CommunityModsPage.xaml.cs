using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Mvvm.Contracts;
using System.Windows;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for CommunityModsPage.xaml
    /// </summary>
    public partial class CommunityModsPage
    {
        private CommunityModsViewModel _viewModel = null!;

        public CommunityModsPage()
        {
            _viewModel = new CommunityModsViewModel();
            DataContext = _viewModel;

            _viewModel.OpenModsEvent += OpenMods;
            _viewModel.OpenModGeneratorEvent += OpenModGenerator;
            _viewModel.OpenPresetModsEvent += OpenPresetMods;

            InitializeComponent();
            App.FrostRPC?.SetPage("Community Mods");
        }

        private void OpenMods(object? sender, EventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
            {
                window.Navigate(typeof(ModsPage));
            }
        }

        private void OpenModGenerator(object? sender, EventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
            {
                window.Navigate(typeof(ModGeneratorPage));
            }
        }

        private void OpenPresetMods(object? sender, EventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
            {
                window.Navigate(typeof(ModsPresetsPage));
            }
        }
    }
}
using Bloxstrap.UI.ViewModels.Settings;
using System.Windows.Input;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class CompetitivePage
    {
        public CompetitivePage()
        {
            DataContext = new CompetitivePageViewModel();
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                var viewModel = (CompetitivePageViewModel)DataContext;
                viewModel.StartSessionPolling();
                await viewModel.RefreshWarpStateAsync();
            };
            Unloaded += (_, _) => ((CompetitivePageViewModel)DataContext).StopSessionPolling();

            App.FrostRPC?.SetPage("Roblox Settings");
        }

        // input validation for the merged Roblox Global Settings (GBS) text boxes
        private void ValidateUInt32(object sender, TextCompositionEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                string newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
                e.Handled = !uint.TryParse(newText, out _);
            }
        }

        private void ValidateFloat(object sender, TextCompositionEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                string newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
                e.Handled = !Regex.IsMatch(newText, @"^-?\d*\.?\d*$");
            }
        }
    }
}

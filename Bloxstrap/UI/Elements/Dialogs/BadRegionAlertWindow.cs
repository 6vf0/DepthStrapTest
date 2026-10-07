using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Bloxstrap.UI.Elements.Dialogs
{
    internal sealed class BadRegionAlertWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };
        public BadRegionAlertWindow(string title, string message)
        {
            Title = "DepthStrap — Server region"; Width = 390; SizeToContent = SizeToContent.Height;
            ShowActivated = false; ShowInTaskbar = false; Topmost = true;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(30, 20, 21));
            Foreground = Brushes.White; FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 12, 0, 16), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            var close = new Button { Content = "Dismiss", Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => Close(); panel.Children.Add(close);
            Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(210, 132, 116)), BorderThickness = new Thickness(1), Child = panel };
            Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - ActualWidth - 20; Top = area.Bottom - ActualHeight - 20; _timer.Start(); };
            _timer.Tick += (_, _) => Close(); Closed += (_, _) => _timer.Stop();
        }
    }
}

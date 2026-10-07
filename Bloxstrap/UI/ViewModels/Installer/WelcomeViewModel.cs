namespace Bloxstrap.UI.ViewModels.Installer
{
    public class WelcomeViewModel : NotifyPropertyChangedViewModel
    {
        // formatting is done here instead of in xaml, it's just a bit easier
        public string MainText => "# Welcome to DepthStrap\n\nA launcher built for your next journey into the depths.\n\nInstallation checks your region routes and sets your FPS cap to your monitor's maximum supported refresh rate. As you play, local session history helps refine your preferred regions for each time of day.\n\nChoose Competitive for quality 3 and minimal rendering load, or Balanced for more detail. You can change your theme and settings at any time.";

        public bool CanContinue { get; set; } = false;
    }
}

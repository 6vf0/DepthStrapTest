using System.Drawing;
namespace Bloxstrap.Extensions
{
    static class BootstrapperIconEx
    {
        public static IReadOnlyCollection<BootstrapperIcon> Selections => new[] { BootstrapperIcon.IconDepthStrap };
        // Legacy saved IDs resolve to the new brand, including the tray and installer.
        public static Icon GetIcon(this BootstrapperIcon icon) => Properties.Resources.IconDepthStrap;
    }
}

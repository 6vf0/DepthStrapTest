namespace Bloxstrap.Extensions
{
    static class BootstrapperStyleEx
    {
        public static IBootstrapperDialog GetNew(this BootstrapperStyle style) => Frontend.GetBootstrapperDialog(style);
        public static IReadOnlyCollection<BootstrapperStyle> Selections => new[] { BootstrapperStyle.DepthStrapDialog };
    }
}

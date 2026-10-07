namespace Bloxstrap.Networking
{
    internal static class NetworkCalibrationProfile
    {
        internal const int PassesPerRoute = 3;
        internal const int ProbesPerTarget = 24;
        internal const int ProbeTimeoutMs = 1200;
        internal const int ProbeSpacingMs = 300;
        internal const int MinimumPassSeconds = 45;
        internal const int SettlingMs = 15000;
        internal const int TransitionSeconds = 120;
        internal const int BudgetMinutes = 45;
    }
}

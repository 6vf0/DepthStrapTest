using Bloxstrap.Integrations;

namespace Bloxstrap.Models
{
    public class ServerEntry
    {
        public int Number { get; set; }
        public string ServerId { get; set; } = null!;
        public string Players { get; set; } = null!;
        public string Region { get; set; } = null!;
        public int? DataCenterId { get; set; }
        public string Uptime { get; set; } = "Loading...";

        /// <summary>Region Preference Score (preferred-region mode only). Not a ping measurement.</summary>
        public int? Score { get; set; }

        /// <summary>RegionQuality name for display (preferred-region mode only).</summary>
        public string Quality { get; set; } = "";

        public System.Windows.Input.ICommand? JoinCommand { get; set; }
    }
}

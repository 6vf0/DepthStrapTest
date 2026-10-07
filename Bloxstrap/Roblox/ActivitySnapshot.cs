using Bloxstrap.Models.Entities;

namespace Bloxstrap.Roblox
{
    /// <summary>
    /// Immutable copy of ActivityData taken the moment OnGameJoin fires.
    /// Never hold a mutable ActivityData reference across async work - the watcher may
    /// mutate it again during the next teleport while diagnostics are still running.
    /// </summary>
    public sealed record ActivitySnapshot(
        long UniverseId,
        long PlaceId,
        string JobId,
        bool IsTeleport,
        ServerType ServerType,
        string MachineAddress,
        string? UdmuxAddress,
        int? UdmuxPort,
        string? RccAddress,
        int? RccPort,
        DateTime Timestamp)
    {
        public static ActivitySnapshot From(ActivityData data) => new(
            UniverseId: data.UniverseId,
            PlaceId: data.PlaceId,
            JobId: data.JobId ?? "",
            IsTeleport: data.IsTeleport,
            ServerType: data.ServerType,
            MachineAddress: data.MachineAddress ?? "",
            UdmuxAddress: data.UdmuxAddress,
            UdmuxPort: data.UdmuxPort,
            RccAddress: data.RccAddress,
            RccPort: data.RccPort,
            Timestamp: DateTime.Now);

        /// <summary>Preferred live network endpoint: UDMUX when present, else the direct machine address.</summary>
        public string LiveEndpoint => !string.IsNullOrEmpty(UdmuxAddress) ? UdmuxAddress : MachineAddress;
    }
}

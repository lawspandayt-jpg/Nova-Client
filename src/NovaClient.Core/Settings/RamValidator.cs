using System.Runtime.InteropServices;

namespace NovaClient.Core.Settings;

/// <summary>
/// RAM allocation rules: at least 1 GB for 1.8.9, and never more than total physical memory minus
/// a 2 GB reserve for Windows + the launcher, so users cannot starve their system.
/// </summary>
public static class RamValidator
{
    public const int MinimumMb = 1024;
    private const int SystemReserveMb = 2048;

    public static int TotalPhysicalMb { get; } = QueryTotalPhysicalMb();

    public static int MaximumMb => Math.Max(MinimumMb, TotalPhysicalMb - SystemReserveMb);

    public static int Clamp(int requestedMb) => Math.Clamp(requestedMb, MinimumMb, MaximumMb);

    /// <summary>Recommendation: 2 GB for ≤8 GB systems, 3 GB for ≤16 GB, 4 GB above that.</summary>
    public static int Recommended()
    {
        if (TotalPhysicalMb <= 8192) return Math.Min(2048, MaximumMb);
        if (TotalPhysicalMb <= 16384) return 3072;
        return 4096;
    }

    // Exposed for tests: pure clamp logic against an arbitrary total.
    public static int ClampAgainst(int requestedMb, int totalPhysicalMb)
    {
        var max = Math.Max(MinimumMb, totalPhysicalMb - SystemReserveMb);
        return Math.Clamp(requestedMb, MinimumMb, max);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static int QueryTotalPhysicalMb()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
                return (int)(status.ullTotalPhys / (1024 * 1024));
        }
        catch { }
        return 8192; // safe assumption if the query fails
    }
}

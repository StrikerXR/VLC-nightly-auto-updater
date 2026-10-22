using System.Runtime.InteropServices;

namespace VlcNightlyUpdater.Services;

public enum VlcArch { X64, Arm64 }

public static class ArchInfo
{
    /// <summary>What the Windows install itself is (not what this process is: a 32-bit process on 64-bit Windows still reports 64-bit here).</summary>
    public static VlcArch Native => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => VlcArch.Arm64,
        _ => VlcArch.X64
    };

    /// <summary>Which build of THIS updater is running (decides which setup file self-update downloads).</summary>
    public static VlcArch ProcessArch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => VlcArch.Arm64,
        _ => VlcArch.X64
    };

    /// <summary>VLC builds this machine can run: 64-bit everywhere (primary), plus ARM64 on ARM64 Windows.</summary>
    public static IReadOnlyList<VlcArch> Supported => Native switch
    {
        VlcArch.Arm64 => [VlcArch.X64, VlcArch.Arm64],
        _ => [VlcArch.X64]
    };

    public static string Key(this VlcArch a) => a switch { VlcArch.X64 => "x64", _ => "arm64" };
    public static string Label(this VlcArch a) => a switch { VlcArch.X64 => "64-bit", _ => "ARM64" };

    public static VlcArch? ParseKey(string? s) => s?.ToLowerInvariant() switch
    {
        "x64" => VlcArch.X64,
        "arm64" => VlcArch.Arm64,
        _ => null
    };

    /// <summary>Reads the CPU type out of an .exe's PE header, so we know what's really installed.</summary>
    public static VlcArch? ReadPeArch(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (fs.Length < 0x40) return null;

            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset + 6 > fs.Length) return null;

            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return null; // "PE\0\0"

            return br.ReadUInt16() switch
            {
                0x8664 => VlcArch.X64,
                0xAA64 or 0xA641 => VlcArch.Arm64, // ARM64 / ARM64EC
                _ => null
            };
        }
        catch { return null; }
    }
}

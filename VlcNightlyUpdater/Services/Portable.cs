namespace VlcNightlyUpdater.Services;

/// <summary>
/// Portable mode. A copy is portable when a file named "portable.flag" sits next to the exe (the portable .zip ships
/// with one). Portable = everything it saves lives in a "Data" folder beside the exe, so you can run it from a USB
/// stick or a synced folder and delete the folder to remove every trace. A portable copy never installs itself over
/// anything and never creates the scheduled task; it tells you when a new version exists and links to the download.
/// </summary>
public static class Portable
{
    public const string MarkerFile = "portable.flag";

    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFile));

    /// <summary>Where a portable copy keeps state.json and the installer cache.</summary>
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");
}

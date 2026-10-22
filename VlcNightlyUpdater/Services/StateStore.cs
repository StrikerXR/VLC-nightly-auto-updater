using System.Text.Json;

namespace VlcNightlyUpdater.Services;

public sealed class AppState
{
    /// <summary>Folder name of the nightly we last installed/assumed, e.g. 20261021-0426.</summary>
    public string? InstalledBuild { get; set; }

    /// <summary>Git commit the installed build was made from (parsed from the installer's file name).</summary>
    public string? InstalledHash { get; set; }

    /// <summary>"x64" or "arm64".</summary>
    public string? InstalledArch { get; set; }

    /// <summary>The build that was installed before the current one (powers "what changed in my last update").</summary>
    public string? PreviousBuild { get; set; }
    public string? PreviousHash { get; set; }

    public DateTime? LastChecked { get; set; }

    /// <summary>true = install updates as soon as the app opens; false = show "Update now" first.</summary>
    public bool AutoUpdate { get; set; } = true;

    public bool LaunchAfterUpdate { get; set; }
    public bool CloseAfterUpdate { get; set; }

    /// <summary>If VLC had to be closed for the update, start it again afterwards.</summary>
    public bool ReopenIfWasRunning { get; set; } = true;

    /// <summary>"auto" (match whatever VLC is installed), or "x64" / "arm64".</summary>
    public string ArchOverride { get; set; } = "auto";

    /// <summary>Update this updater itself from GitHub Releases without asking.</summary>
    public bool AutoUpdateSelf { get; set; } = true;
}

/// <summary>Plain JSON in %LOCALAPPDATA% (unpackaged apps can't use ApplicationData.LocalSettings).</summary>
public static class StateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VlcNightlyUpdater");
    private static readonly string FilePath = Path.Combine(Dir, "state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
        }
        catch { /* corrupt file -> treat as first run */ }
        return new AppState();
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // Write-then-swap so a crash mid-write can't leave a half-written state.json.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* non-fatal */ }
    }
}

using System.Text.Json;

namespace VlcNightlyUpdater.Services;

public sealed class AppState
{
    /// <summary>Folder name of the nightly we last installed/assumed, e.g. 20261021-0426.</summary>
    public string? InstalledBuild { get; set; }
}

/// <summary>Plain JSON in %LOCALAPPDATA% (unpackaged apps can't use ApplicationData.LocalSettings).</summary>
public static class StateStore
{
    private static readonly string Dir =
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
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }
}

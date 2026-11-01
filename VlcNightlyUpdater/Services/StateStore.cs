using System.Text.Json;

namespace VlcNightlyUpdater.Services;

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

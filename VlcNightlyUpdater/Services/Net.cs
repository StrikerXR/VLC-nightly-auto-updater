namespace VlcNightlyUpdater.Services;

/// <summary>One shared HttpClient + the running app's version.</summary>
public static class Net
{
    /// <summary>MAJOR.MINOR.BUILD of this running copy (e.g. 2.0.57).</summary>
    public static readonly Version CurrentVersion = ReadVersion();

    public static readonly HttpClient Http = Create();

    private static Version ReadVersion()
    {
        var v = typeof(Net).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    private static HttpClient Create()
    {
        // Timeout covers "until headers arrive" for streamed downloads and the whole request for small pages.
        var c = new HttpClient { Timeout = AppConfig.HttpTimeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"VlcNightlyUpdater/{CurrentVersion}");
        return c;
    }
}

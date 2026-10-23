using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace VlcNightlyUpdater.Services;

/// <summary>
/// Optional "check for VLC updates in the background" (OFF by default).
///
/// While it is off, NOTHING exists: no scheduled task, no startup entry, no service, no registry keys.
/// While it is on, there is exactly one per-user Windows scheduled task that starts this exe with
/// --background-check at sign-in and every few hours. That run has no window: it asks VideoLAN for the latest
/// nightly, shows a Windows notification if it's newer than what you have, and exits. It never installs anything.
///
/// Everything this class creates is removed again by <see cref="Disable"/>, and installer.iss removes the same
/// names on uninstall. If you add anything here, add it to both.
/// </summary>
public static class BackgroundCheck
{
    public const string Flag = "--background-check";

    // Keep in sync with installer.iss ([Code] section).
    public const string TaskName = "VlcNightlyUpdater Background Check";
    public const string Aumid = "StrikerXR.VlcNightlyUpdater";
    public const string Protocol = "vlcnightlyupdater";

    private const string AumidKey = @"Software\Classes\AppUserModelId\" + Aumid;
    private const string ProtocolKey = @"Software\Classes\" + Protocol;
    // Created by Windows itself the first time a toast is shown for our AUMID.
    private const string NotifySettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + Aumid;

    private const int EveryHours = 6;

    public static bool IsBackgroundRun(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Couldn't work out where this app is installed.");

    // ---------- turn on / off ----------

    /// <summary>Creates the scheduled task and the notification registration. Safe to call repeatedly.</summary>
    public static void Enable()
    {
        try
        {
            RegisterNotifications();
            CreateTask();
        }
        catch
        {
            Disable(); // never leave half of it behind
            throw;
        }
    }

    /// <summary>Removes the scheduled task and every registry key this feature creates. Safe if none exist.</summary>
    public static void Disable()
    {
        try { Schtasks($"/Delete /TN \"{TaskName}\" /F"); } catch { }
        foreach (var key in new[] { AumidKey, ProtocolKey, NotifySettingsKey })
            try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); } catch { }
    }

    private static void RegisterNotifications()
    {
        var exe = ExePath;
        var dir = Path.GetDirectoryName(exe)!;

        // Lets Windows show "VLC Nightly Updater" + our icon as the sender of the notification.
        using (var k = Registry.CurrentUser.CreateSubKey(AumidKey))
        {
            k.SetValue("DisplayName", "VLC Nightly Updater");
            k.SetValue("IconUri", Path.Combine(dir, "Assets", "app.ico"));
        }

        // Clicking the notification opens vlcnightlyupdater:open, which starts the app.
        using (var k = Registry.CurrentUser.CreateSubKey(ProtocolKey))
        {
            k.SetValue("", "URL:VLC Nightly Updater");
            k.SetValue("URL Protocol", "");
            using var cmd = k.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
    }

    private static void CreateTask()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape($"\"{ExePath}\"");
        var start = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");

        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Checks for a newer VLC nightly and shows a notification. Created by VLC Nightly Updater; removed when you turn the option off or uninstall.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT2M</Delay>
                </LogonTrigger>
                <TimeTrigger>
                  <Repetition>
                    <Interval>PT{EveryHours}H</Interval>
                  </Repetition>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>{Flag}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), $"vlcnu-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode); // schtasks wants UTF-16
            var (code, output) = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            if (code != 0)
                throw new InvalidOperationException($"Windows wouldn't create the scheduled task: {output}".Trim());
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static (int Code, string Output) Schtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start schtasks.exe.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20_000))
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("schtasks.exe didn't answer.");
        }
        return (p.ExitCode, (output.Result + " " + error.Result).Trim());
    }

    // ---------- the headless run ----------

    /// <summary>What the scheduled task runs. No window. Checks, maybe notifies, returns.</summary>
    public static async Task RunAsync()
    {
        var state = StateStore.Load();

        // The switch is the source of truth: if it's off, clean up anything stale and do nothing at all.
        if (!state.BackgroundCheck)
        {
            Disable();
            return;
        }

        try
        {
            if (state.OnStable) { Record("skipped (VLC is on the stable release)"); return; }
            if (state.IsPaused) { Record($"paused until {state.PausedUntil:MMM d}"); return; }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = cts.Token;

            var vlc = VlcLocator.Find();
            if (vlc is null || state.InstalledBuild is null) { Record("skipped (no VLC build to compare against yet)"); return; }

            var target = ArchInfo.ParseKey(state.ArchOverride) ?? vlc.Arch ?? ArchInfo.Native;
            if (!ArchInfo.Supported.Contains(target)) target = ArchInfo.Native;
            if (vlc.Arch is { } have && have != target) { Record("skipped (CPU type differs, switching is your call)"); return; }

            var svc = new UpdateService { Arch = target };
            var folders = await svc.ListFoldersAsync(ct).ConfigureAwait(false);
            var latest = await svc.GetLatestAsync(folders, ct).ConfigureAwait(false);

            if (string.CompareOrdinal(state.InstalledBuild, latest.Folder) >= 0) { Record("up to date"); return; }

            // Re-read so a change the app made while we were checking isn't overwritten, and only tell them once per build.
            state = StateStore.Load();
            if (!state.BackgroundCheck || state.IsPaused) return;
            if (state.LastNotifiedBuild == latest.Folder) { Record($"{latest.Folder} is new (already notified)"); return; }

            if (Show("New VLC nightly available",
                     $"{latest.Folder} ({target.Label()}). Open VLC Nightly Updater to install it.", "vlc-update"))
            {
                state.LastNotifiedBuild = latest.Folder;
                StateStore.Save(state);
                Record($"notified about {latest.Folder}");
            }
            else Record($"{latest.Folder} is new, but Windows refused the notification");
        }
        catch (Exception ex)
        {
            Record("failed: " + ex.Message);
            throw;
        }
        finally
        {
            await Task.Delay(1500).ConfigureAwait(false); // give Windows time to take the toast before the process exits
        }
    }

    /// <summary>Remembers the outcome so the app can show "Last background check: ..." (proof it's actually running).</summary>
    private static void Record(string what)
    {
        try
        {
            var s = StateStore.Load();
            s.LastBackgroundRun = $"{DateTime.Now:MMM d, h:mm tt} \u00B7 {what}";
            StateStore.Save(s);
        }
        catch { }
    }

    /// <summary>Shows a harmless test toast. Returns false if Windows didn't accept it.</summary>
    public static bool SendTest()
    {
        RegisterNotifications();
        return Show("Notifications are working", "This is how a new VLC nightly will be announced.", "vlc-test");
    }

    private static bool Show(string title, string body, string tag)
    {
        try
        {
            RegisterNotifications(); // in case it was cleaned away

            static string Esc(string s) => SecurityElement.Escape(s) ?? "";
            var xml = $"""
                <toast activationType="protocol" launch="{Protocol}:open">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{Esc(title)}</text>
                      <text>{Esc(body)}</text>
                    </binding>
                  </visual>
                </toast>
                """;

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var toast = new ToastNotification(doc)
            {
                Tag = tag,                       // a newer one replaces an older one instead of stacking
                Group = "vlc",
                ExpirationTime = DateTimeOffset.Now.AddDays(3)
            };
            var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
            notifier.Show(toast);
            return notifier.Setting == NotificationSetting.Enabled; // false when blocked in Windows settings
        }
        catch { return false; }
    }
}

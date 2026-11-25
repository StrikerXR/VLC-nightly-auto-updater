using VlcNightlyUpdater.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VlcNightlyUpdater;

/// <summary>"Copy diagnostics" (Settings): puts a short plain-text report on the clipboard. The text is built in Services/DiagnosticsReport.cs.</summary>
public sealed partial class MainWindow
{
    private void CopyDiagnostics()
    {
        var ok = false;
        try
        {
            var text = DiagnosticsReport.Build(_state, _target, _latest?.Folder, Status.CurrentTitle, Status.CurrentDetail, Status.CurrentPhase.ToString());
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            ok = true;
            try { Clipboard.Flush(); } catch { /* keeps the text after the app closes; fine if it can't */ }
        }
        catch { /* clipboard busy or unavailable: the button says so */ }

        _ = Settings.ShowDiagnosticsResultAsync(ok);
    }
}

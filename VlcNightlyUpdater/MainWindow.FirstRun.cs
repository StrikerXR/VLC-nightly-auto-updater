using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

/// <summary>
/// First run with VLC already installed: the updater has no record of which nightly that is, so it asks once.
/// Yes = keep it (record the newest nightly as installed). No or "I don't know" = install the newest nightly now,
/// because a possibly-old build is the riskier guess. Closing the dialog counts as "I don't know".
/// The question is asked again on the next launch if nothing was installed or recorded. Wording lives in Strings.cs.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>True = the user says VLC is already the newest nightly. False = No, "I don't know" or dialog dismissed.</summary>
    private async Task<bool> AskIsLatestAsync(BuildInfo latest, VlcArch target)
    {
        var result = await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.FirstRunQuestion,
            Content = Strings.FirstRunQuestionBody(latest.Folder, target),
            PrimaryButtonText = Strings.AnswerYes,
            SecondaryButtonText = Strings.AnswerNo,
            CloseButtonText = Strings.AnswerUnsure,
            DefaultButton = ContentDialogButton.Close // the safe answer is the one Enter picks
        }.ShowAsync();

        return result == ContentDialogResult.Primary;
    }
}

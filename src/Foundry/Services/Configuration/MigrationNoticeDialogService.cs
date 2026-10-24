// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Shows the pending migration notices in one dialog over the main window, at most once per session; closing the dialog
/// dismisses every notice it listed.
/// </summary>
/// <remarks>Call on the UI thread. The dialog waits until the main window is loaded and until any other open dialog closes.</remarks>
internal sealed class MigrationNoticeDialogService(
    IMigrationNoticeService notices,
    IDialogService dialogs,
    IApplicationLocalizationService localization,
    ILogger logger)
{
    /// <summary>The notices in display order, with the resource key of their text.</summary>
    private static readonly (MigrationNotice Notice, string TextKey)[] NoticeTexts =
    [
        (MigrationNotice.PasswordsNeeded, "Sharing.MigrationPasswordsNeeded"),
        (MigrationNotice.LegacySyncRetired, "Sharing.MigrationLegacySyncRetired"),
        (MigrationNotice.FilesToReselect, "Sharing.MigrationFilesToReselect"),
    ];

    private readonly ILogger logger = logger.ForContext<MigrationNoticeDialogService>();
    private bool hasRun;

    /// <summary>
    /// Shows the notices that are pending now, if any, over <paramref name="rootElement"/>; later calls in the session do nothing.
    /// Never throws: a failure is logged and the notices stay pending for the next launch.
    /// </summary>
    /// <param name="rootElement">Main window root whose XamlRoot hosts the dialog.</param>
    public async Task ShowPendingAsync(FrameworkElement rootElement)
    {
        if (hasRun) return;
        hasRun = true;
        (MigrationNotice Notice, string TextKey)[] shown = NoticeTexts.Where(entry => notices.IsShown(entry.Notice)).ToArray();
        if (shown.Length == 0) return;

        MigrationNotice[] shownNotices = shown.Select(entry => entry.Notice).ToArray();
        try
        {
            await WaitUntilLoadedAsync(rootElement);
            await WaitForOpenDialogsAsync(rootElement.XamlRoot);
            await dialogs.ShowMessageAsync(new DialogRequest(
                localization.GetString("Sharing.MigrationNoticesTitle"),
                string.Join(Environment.NewLine + Environment.NewLine, shown.Select(entry => localization.GetString(entry.TextKey))),
                localization.GetString("Common.Close")));
            foreach (MigrationNotice notice in shownNotices) notices.Dismiss(notice);
            logger.Information("Migration notices shown and dismissed. Notices={Notices}", shownNotices);
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Migration notices could not be shown; they are shown again at the next launch. Notices={Notices}", shownNotices);
        }
    }

    private static Task WaitUntilLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded) return Task.CompletedTask;

        // ContentDialog needs the window content's XamlRoot, which is only reliable once the root element is loaded.
        TaskCompletionSource loaded = new();
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            element.Loaded -= OnLoaded;
            loaded.TrySetResult();
        }

        element.Loaded += OnLoaded;
        return loaded.Task;
    }

    /// <summary>Waits until no other ContentDialog is open: WinUI allows a single open ContentDialog per window.</summary>
    private static async Task WaitForOpenDialogsAsync(XamlRoot xamlRoot)
    {
        var observed = new HashSet<ContentDialog>();
        while (VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Select(popup => popup.Child).OfType<ContentDialog>().FirstOrDefault() is { } open)
        {
            if (observed.Add(open))
            {
                TaskCompletionSource closed = new();
                void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
                {
                    sender.Closed -= OnClosed;
                    closed.TrySetResult();
                }

                open.Closed += OnClosed;
                await closed.Task;
            }
            else
            {
                // The closed dialog's popup has not left the tree yet; check again shortly.
                await Task.Delay(100);
            }
        }
    }
}

using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Services.Autopilot;

public sealed class AutopilotTenantDownloadDialogService(
    IApplicationLocalizationService localizationService) : IAutopilotTenantDownloadDialogService
{
    public async Task<IReadOnlyList<AutopilotProfileSettings>?> DownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<AutopilotProfileSettings>>> downloadProfilesAsync)
    {
        ArgumentNullException.ThrowIfNull(downloadProfilesAsync);

        using var cancellationTokenSource = new CancellationTokenSource();
        ContentDialog dialog = CreateDialog(cancellationTokenSource);
        Task<IReadOnlyList<AutopilotProfileSettings>> downloadTask = downloadProfilesAsync(cancellationTokenSource.Token);
        Task<ContentDialogResult> dialogTask = dialog.ShowAsync().AsTask();

        Task completedTask = await Task.WhenAny(downloadTask, dialogTask);
        if (completedTask == dialogTask)
        {
            cancellationTokenSource.Cancel();
            _ = ObserveDownloadTaskAsync(downloadTask);
            return null;
        }

        dialog.DispatcherQueue.TryEnqueue(dialog.Hide);
        await dialogTask;
        return await downloadTask;
    }

    private ContentDialog CreateDialog(CancellationTokenSource cancellationTokenSource)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = localizationService.GetString("Autopilot.TenantDownloadDialogTitle"),
            Content = CreateDialogContent(),
            CloseButtonText = localizationService.GetString("Autopilot.TenantDownloadDialogCancel"),
            DefaultButton = ContentDialogButton.Close
        };

        dialog.CloseButtonClick += (_, _) => cancellationTokenSource.Cancel();
        return dialog;
    }

    private FrameworkElement CreateDialogContent()
    {
        return new StackPanel
        {
            MinWidth = 360,
            Spacing = 16,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.ProgressRing
                {
                    Width = 48,
                    Height = 48,
                    IsActive = true,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = localizationService.GetString("Autopilot.TenantDownloadDialogMessage"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
    }

    private static async Task ObserveDownloadTaskAsync(Task<IReadOnlyList<AutopilotProfileSettings>> downloadTask)
    {
        try
        {
            await downloadTask.ConfigureAwait(false);
        }
        catch
        {
            // The user canceled the dialog; late task completion is intentionally ignored.
        }
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Coordinates shared Microsoft Graph tenant operation progress and cancellation dialogs.
/// </summary>
public sealed class AutopilotTenantOperationDialogService(
    IApplicationLocalizationService localizationService) : IAutopilotTenantOperationDialogService
{
    /// <inheritdoc />
    public Task<TResult?> RunAsync<TResult>(
        string title,
        string message,
        Func<CancellationToken, Task<TResult>> operationAsync)
        where TResult : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(operationAsync);

        return RunWithDialogAsync(
            title,
            message,
            localizationService.GetString("Common.Cancel"),
            operationAsync);
    }

    private static async Task<TResult?> RunWithDialogAsync<TResult>(
        string title,
        string message,
        string cancelText,
        Func<CancellationToken, Task<TResult>> operationAsync)
        where TResult : class
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        ContentDialog dialog = CreateDialog(title, message, cancelText, cancellationTokenSource);
        TaskCompletionSource dialogOpenedTask = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource dialogCanceledTask = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += OnDialogOpened;
        dialog.Closing += OnDialogClosing;

        try
        {
            Task<ContentDialogResult> dialogTask = dialog.ShowAsync().AsTask();
            Task completedBeforeOpenTask = await Task.WhenAny(dialogOpenedTask.Task, dialogCanceledTask.Task, dialogTask);
            if (completedBeforeOpenTask != dialogOpenedTask.Task)
            {
                cancellationTokenSource.Cancel();
                await ObserveDialogTaskAsync(dialogTask);
                return default;
            }

            Task<TResult> operationTask = Task.Run(
                () => operationAsync(cancellationTokenSource.Token),
                cancellationTokenSource.Token);
            Task completedTask = await Task.WhenAny(operationTask, dialogCanceledTask.Task, dialogTask);
            if (completedTask != operationTask)
            {
                cancellationTokenSource.Cancel();
                _ = ObserveTaskAsync(operationTask);
                await CloseDialogWithoutBlockingAsync(dialog, dialogTask);
                return default;
            }

            try
            {
                TResult result = await operationTask;
                await CloseDialogWithoutBlockingAsync(dialog, dialogTask);
                return result;
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                await CloseDialogWithoutBlockingAsync(dialog, dialogTask);
                return default;
            }
            catch
            {
                await CloseDialogWithoutBlockingAsync(dialog, dialogTask);
                throw;
            }
        }
        finally
        {
            dialog.Opened -= OnDialogOpened;
            dialog.Closing -= OnDialogClosing;
        }

        void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            dialogOpenedTask.TrySetResult();
        }

        void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            cancellationTokenSource.Cancel();
            dialogCanceledTask.TrySetResult();
        }
    }

    private static ContentDialog CreateDialog(string title, string message, string cancelText, CancellationTokenSource cancellationTokenSource)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = title,
            Content = CreateDialogContent(message),
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close
        };

        dialog.CloseButtonClick += (_, _) => cancellationTokenSource.Cancel();
        return dialog;
    }

    private static FrameworkElement CreateDialogContent(string message)
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
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
    }

    private static async Task ObserveTaskAsync<TResult>(Task<TResult> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The user canceled the dialog; late task completion is intentionally ignored.
        }
    }

    private static async Task ObserveDialogTaskAsync(Task<ContentDialogResult> dialogTask)
    {
        try
        {
            await dialogTask;
        }
        catch
        {
            // Dialog teardown after cancellation should not keep the tenant operation alive.
        }
    }

    private static async Task CloseDialogWithoutBlockingAsync(ContentDialog dialog, Task<ContentDialogResult> dialogTask)
    {
        if (!dialogTask.IsCompleted)
        {
            dialog.Hide();
        }

        await Task.WhenAny(dialogTask, Task.Delay(TimeSpan.FromSeconds(2)));
    }
}

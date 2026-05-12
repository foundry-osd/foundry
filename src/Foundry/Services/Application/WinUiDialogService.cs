using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

/// <summary>
/// Displays simple WinUI message and confirmation dialogs for application services and view models.
/// </summary>
public sealed class WinUiDialogService : IDialogService
{
    /// <inheritdoc />
    public async Task ShowMessageAsync(DialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = CreateMessageContent(request.Message),
            CloseButtonText = request.CloseButtonText,
            XamlRoot = GetXamlRoot()
        };

        await dialog.ShowAsync();
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(ConfirmationDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = CreateMessageContent(request.Message),
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CancelButtonText,
            XamlRoot = GetXamlRoot()
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static TextBlock CreateMessageContent(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 360,
            MaxWidth = 520,
            Margin = new Thickness(0, 4, 0, 8)
        };
    }

    private static XamlRoot GetXamlRoot()
    {
        return App.MainWindow.Content.XamlRoot;
    }
}

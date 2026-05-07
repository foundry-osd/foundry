using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

public sealed class WinUiDialogService : IDialogService
{
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
            MaxWidth = 520
        };
    }

    private static XamlRoot GetXamlRoot()
    {
        return App.MainWindow.Content.XamlRoot;
    }
}

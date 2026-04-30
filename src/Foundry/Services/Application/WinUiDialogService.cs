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
            Content = request.Message,
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
            Content = request.Message,
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CancelButtonText,
            XamlRoot = GetXamlRoot()
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static XamlRoot GetXamlRoot()
    {
        return App.MainWindow.Content.XamlRoot;
    }
}

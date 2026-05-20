using Foundry.Services.Localization;

namespace Foundry.Services.Autopilot;

public sealed class AutopilotCertificateDialogService(
    IApplicationLocalizationService localizationService) : IAutopilotCertificateDialogService
{
    public async Task ShowCreatedAsync(string pfxOutputPath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedTitle"),
            Content = CreateContent(pfxOutputPath, password),
            CloseButtonText = localizationService.GetString("Common.Close"),
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private FrameworkElement CreateContent(string pfxOutputPath, string password)
    {
        return new StackPanel
        {
            MinWidth = 480,
            MaxWidth = 560,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedInstruction"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                new TextBlock
                {
                    Text = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedPathLabel"),
                    Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyStrongTextBlockStyle"]
                },
                new TextBlock
                {
                    Text = pfxOutputPath,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                new TextBlock
                {
                    Text = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedPasswordLabel"),
                    Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyStrongTextBlockStyle"],
                    Margin = new Thickness(0, 8, 0, 0)
                },
                new Microsoft.UI.Xaml.Controls.TextBox
                {
                    Text = password,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                }
            }
        };
    }
}

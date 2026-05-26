using Foundry.Services.Localization;
using Windows.ApplicationModel.DataTransfer;

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
                CreatePasswordRow(password)
            }
        };
    }

    private FrameworkElement CreatePasswordRow(string password)
    {
        var copiedTextBlock = new TextBlock
        {
            Text = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedPasswordCopied"),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorSuccessBrush"],
            Visibility = Visibility.Collapsed
        };

        var copyButton = new Button
        {
            Content = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedCopyPasswordButton"),
            MinWidth = 128
        };
        Grid.SetColumn(copyButton, 1);

        copyButton.Click += (_, _) =>
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(password);
            Clipboard.SetContent(dataPackage);
            copiedTextBlock.Visibility = Visibility.Visible;
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Children =
                    {
                        new Microsoft.UI.Xaml.Controls.TextBox
                        {
                            Text = password,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.NoWrap,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                        },
                        copyButton
                    }
                },
                copiedTextBlock
            }
        };
    }
}

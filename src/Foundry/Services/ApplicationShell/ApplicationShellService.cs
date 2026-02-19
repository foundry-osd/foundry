using Foundry.Services.Localization;
using Microsoft.Win32;
using System.Windows;

namespace Foundry.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    private readonly ILocalizationService _localizationService;

    public ApplicationShellService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public void Shutdown()
    {
        Application.Current.Shutdown();
    }

    public void ShowAbout()
    {
        StringsWrapper strings = _localizationService.Strings;
        MessageBox.Show(
            strings["AboutMessage"],
            strings["AboutTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public string? PickIsoOutputPath(string defaultFileName)
    {
        StringsWrapper strings = _localizationService.Strings;
        var dialog = new SaveFileDialog
        {
            Title = strings["IsoPickerTitle"],
            Filter = strings["IsoPickerFilter"],
            DefaultExt = ".iso",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "foundry-winpe.iso" : defaultFileName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolderPath(string title, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool ConfirmWarning(string title, string message)
    {
        MessageBoxResult result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}

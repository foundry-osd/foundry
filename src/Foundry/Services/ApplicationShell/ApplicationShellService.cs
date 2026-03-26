using Foundry.Models.Configuration;
using Foundry.Services.Localization;
using Foundry.ViewModels;
using Foundry.Views;
using Microsoft.Win32;
using System.Diagnostics;
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

    public void OpenUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
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

    public string? PickOpenFilePath(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = ".json",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFilePath(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = ".json",
            AddExtension = true,
            FileName = defaultFileName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<AutopilotProfileSettings>? PickAutopilotProfilesForImport(IReadOnlyList<AutopilotProfileSettings> availableProfiles)
    {
        ArgumentNullException.ThrowIfNull(availableProfiles);

        var viewModel = new AutopilotProfileSelectionDialogViewModel(_localizationService, availableProfiles);
        var dialog = new AutopilotProfileSelectionDialog
        {
            DataContext = viewModel,
            Owner = ResolveOwnerWindow()
        };

        try
        {
            viewModel.CloseRequested += (_, result) =>
            {
                dialog.DialogResult = result;
                dialog.Close();
            };

            bool? dialogResult = dialog.ShowDialog();
            return dialogResult == true
                ? viewModel.GetSelectedProfiles()
                : null;
        }
        finally
        {
            viewModel.Dispose();
        }
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

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
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

    private static Window? ResolveOwnerWindow()
    {
        if (Application.Current?.Windows is null)
        {
            return Application.Current?.MainWindow;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return Application.Current.MainWindow;
    }
}

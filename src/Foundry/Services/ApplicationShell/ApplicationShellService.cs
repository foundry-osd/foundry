using Microsoft.Win32;
using System.Windows;

namespace Foundry.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    public void Shutdown()
    {
        Application.Current.Shutdown();
    }

    public void ShowAbout()
    {
        MessageBox.Show(
            "Foundry\nWPF .NET 10\nMVVM bootstrap ready.",
            "About Foundry",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public string? PickIsoOutputPath(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Select ISO output path",
            Filter = "ISO image (*.iso)|*.iso",
            DefaultExt = ".iso",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "foundry-winpe.iso" : defaultFileName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
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

using System.Diagnostics;
using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

public sealed class WinUiExternalProcessLauncher : IExternalProcessLauncher
{
    public Task OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Open(uri.AbsoluteUri);
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        Directory.CreateDirectory(folderPath);
        Open(folderPath);
        return Task.CompletedTask;
    }

    public Task OpenFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Open(filePath);
        return Task.CompletedTask;
    }

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }
}

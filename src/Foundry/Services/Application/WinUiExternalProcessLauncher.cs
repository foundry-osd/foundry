using System.Diagnostics;
using Foundry.Core.Services.Application;
using Serilog;

namespace Foundry.Services.Application;

/// <summary>
/// Opens URIs, folders, and files with the Windows shell from WinUI commands.
/// </summary>
public sealed class WinUiExternalProcessLauncher(ILogger logger) : IExternalProcessLauncher
{
    private readonly ILogger logger = logger.ForContext<WinUiExternalProcessLauncher>();

    /// <inheritdoc />
    public Task OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Open(uri.AbsoluteUri, "Uri", GetUriLogValue(uri));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OpenFolderAsync(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        try
        {
            Directory.CreateDirectory(folderPath);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to prepare external target. TargetKind={TargetKind}, Target={Target}", "Folder", folderPath);
            throw;
        }

        Open(folderPath, "Folder", folderPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OpenFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Open(filePath, "File", filePath);
        return Task.CompletedTask;
    }

    private void Open(string target, string targetKind, string targetLogValue)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to open external target. TargetKind={TargetKind}, Target={Target}", targetKind, targetLogValue);
            throw;
        }
    }

    private static string GetUriLogValue(Uri uri)
    {
        UriBuilder builder = new(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        // Strip credentials and query strings before logging externally opened URIs.
        return builder.Uri.ToString().TrimEnd('/');
    }
}

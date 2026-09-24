// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Reads volume headroom and tests directory write access for deployment staging.</summary>
public sealed class DeploymentStorageService : IDeploymentStorageService
{
    /// <inheritdoc />
    public long? GetAvailableBytes(string path)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool CanWriteDirectory(string path, string? existingFilePath = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(existingFilePath) && File.Exists(existingFilePath))
            {
                using var existing = new FileStream(existingFilePath, FileMode.Open, FileAccess.Write, FileShare.Read);
                return true;
            }

            Directory.CreateDirectory(path);
            using var probe = new FileStream(Path.Combine(path, $".foundry-probe-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}

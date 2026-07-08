// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;

namespace Foundry.Core.Services.WinPe;

/// <inheritdoc />
public sealed class WinPePowerShellModuleProvisioningService : IWinPePowerShellModuleProvisioningService
{
    // Windows PowerShell module path inside the boot image: %WINDIR%\System32\WindowsPowerShell\v1.0\Modules.
    // This is on the default WinPE PSModulePath, and modules are laid out as ModuleName\ModuleVersion
    // (the structure produced by Save-Module).
    private const string ModulesRelativePath = @"Windows\System32\WindowsPowerShell\v1.0\Modules";

    // Metadata entries present in a .nupkg that are not part of the module payload.
    private static readonly string[] PackageMetadataPrefixes = ["_rels", "package"];

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes the service with a default HTTP client.
    /// </summary>
    public WinPePowerShellModuleProvisioningService()
        : this(new HttpClient())
    {
    }

    internal WinPePowerShellModuleProvisioningService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<WinPeResult> ProvisionAsync(
        WinPePowerShellModuleProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.MountedImagePath) || !Directory.Exists(options.MountedImagePath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path does not exist.",
                $"Path: '{options.MountedImagePath}'.");
        }

        if (options.Modules.Count == 0)
        {
            return WinPeResult.Success();
        }

        string modulesRoot = Path.Combine(options.MountedImagePath, ModulesRelativePath);

        int index = 0;
        foreach (PowerShellModuleSelection module in options.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            options.DownloadProgress?.Report(new WinPeDownloadProgress
            {
                Percent = null,
                Status = $"Integrating PowerShell module {index} of {options.Modules.Count}: {module.Name}."
            });

            WinPeResult moduleResult = module.Source switch
            {
                PowerShellModuleSource.Gallery => await ProvisionGalleryModuleAsync(module, modulesRoot, options, cancellationToken).ConfigureAwait(false),
                PowerShellModuleSource.Local => ProvisionLocalModule(module, modulesRoot),
                _ => WinPeResult.Failure(WinPeErrorCodes.ValidationFailed, "Unsupported PowerShell module source.", $"Value: '{module.Source}'.")
            };

            if (!moduleResult.IsSuccess)
            {
                return moduleResult;
            }
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ProvisionGalleryModuleAsync(
        PowerShellModuleSelection module,
        string modulesRoot,
        WinPePowerShellModuleProvisioningOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(module.Name) || string.IsNullOrWhiteSpace(module.Version))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "A Gallery module requires a name and version.",
                $"Name: '{module.Name}', Version: '{module.Version}'.");
        }

        string packageUri = $"{options.GalleryBaseUri.TrimEnd('/')}/{Uri.EscapeDataString(module.Name)}/{Uri.EscapeDataString(module.Version)}";
        Directory.CreateDirectory(options.CacheDirectoryPath);
        string cachePath = Path.Combine(options.CacheDirectoryPath, $"{module.Name}.{module.Version}.nupkg");

        if (!File.Exists(cachePath))
        {
            string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.download";
            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream destination = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                TryDeleteFile(temporaryPath);
                return WinPeResult.Failure(
                    WinPeErrorCodes.DownloadFailed,
                    $"Failed to download the '{module.Name}' module from the PowerShell Gallery.",
                    ex.Message);
            }
        }

        string destinationPath = Path.Combine(modulesRoot, module.Name, module.Version);
        try
        {
            ExtractModulePackage(cachePath, destinationPath);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                $"Failed to extract the '{module.Name}' module into the boot image.",
                ex.Message);
        }
    }

    private static WinPeResult ProvisionLocalModule(PowerShellModuleSelection module, string modulesRoot)
    {
        if (string.IsNullOrWhiteSpace(module.LocalPath) || !Directory.Exists(module.LocalPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "A local module requires an existing folder.",
                $"Path: '{module.LocalPath}'.");
        }

        string moduleName = string.IsNullOrWhiteSpace(module.Name)
            ? new DirectoryInfo(module.LocalPath).Name
            : module.Name;
        string destinationPath = string.IsNullOrWhiteSpace(module.Version)
            ? Path.Combine(modulesRoot, moduleName)
            : Path.Combine(modulesRoot, moduleName, module.Version);

        try
        {
            CopyDirectory(module.LocalPath, destinationPath);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                $"Failed to copy the local '{moduleName}' module into the boot image.",
                ex.Message);
        }
    }

    private static void ExtractModulePackage(string packagePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string normalized = entry.FullName.Replace('\\', '/');
            if (IsPackageMetadata(normalized))
            {
                continue;
            }

            string entryDestination = Path.Combine(destinationPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(entryDestination)!);
            entry.ExtractToFile(entryDestination, overwrite: true);
        }
    }

    private static bool IsPackageMetadata(string normalizedEntryPath)
    {
        if (normalizedEntryPath.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedEntryPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string prefix in PackageMetadataPrefixes)
        {
            if (normalizedEntryPath.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        string fullSource = Path.GetFullPath(sourceDirectory);

        foreach (string filePath in Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(fullSource, filePath);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

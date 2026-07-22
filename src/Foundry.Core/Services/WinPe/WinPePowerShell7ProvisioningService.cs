// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;

namespace Foundry.Core.Services.WinPe;

/// <inheritdoc />
public sealed class WinPePowerShell7ProvisioningService : IWinPePowerShell7ProvisioningService
{
    // Runtime install location inside the image (X:\Program Files\PowerShell\7 at boot).
    private const string InstallRelativePath = @"Program Files\PowerShell\7";
    private const string RuntimeInstallPath = @"%ProgramFiles%\PowerShell\7";
    private const string HiveMountName = @"HKLM\FoundryPowerShell7";
    private const string EnvironmentKeyRelativePath = @"ControlSet001\Control\Session Manager\Environment";
    private const string DefaultPsModulePath = @"%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules";

    private readonly HttpClient _httpClient;
    private readonly IWinPeProcessRunner _processRunner;

    /// <summary>
    /// Initializes the service with default HTTP and process runners.
    /// </summary>
    public WinPePowerShell7ProvisioningService()
        : this(new HttpClient(), new WinPeProcessRunner())
    {
    }

    internal WinPePowerShell7ProvisioningService(HttpClient httpClient, IWinPeProcessRunner processRunner)
    {
        _httpClient = httpClient;
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public async Task<WinPeResult> ProvisionAsync(
        WinPePowerShell7ProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeResult<string> archiveResult = await DownloadWithFallbackAsync(options, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.IsSuccess)
        {
            return WinPeResult.Failure(archiveResult.Error!);
        }

        string installPath = Path.Combine(options.MountedImagePath, InstallRelativePath);
        try
        {
            Directory.CreateDirectory(installPath);
            ZipFile.ExtractToDirectory(archiveResult.Value!, installPath, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to extract PowerShell 7 into the boot image.",
                ex.Message);
        }

        WinPeResult environmentResult = await ConfigureEnvironmentAsync(options, cancellationToken).ConfigureAwait(false);
        if (!environmentResult.IsSuccess)
        {
            return environmentResult;
        }

        // ICU is required by the .NET globalization stack that PowerShell 7.5+ uses; WinPE may lack it.
        // Copy it from the build host on a best-effort basis so pwsh does not fail to start.
        TryCopyIcuDependency(options.MountedImagePath);

        return WinPeResult.Success();
    }

    private async Task<WinPeResult<string>> DownloadWithFallbackAsync(
        WinPePowerShell7ProvisioningOptions options,
        CancellationToken cancellationToken)
    {
        WinPeResult<string> primaryResult = await DownloadReleaseAsync(
            options.Release!,
            options.CacheDirectoryPath,
            options.DownloadProgress,
            cancellationToken).ConfigureAwait(false);

        if (primaryResult.IsSuccess)
        {
            return primaryResult;
        }

        // Non-fatal fallback to the latest release when the selected version cannot be downloaded.
        if (options.FallbackRelease is not null &&
            !string.Equals(options.FallbackRelease.DownloadUrl, options.Release!.DownloadUrl, StringComparison.OrdinalIgnoreCase))
        {
            WinPeResult<string> fallbackResult = await DownloadReleaseAsync(
                options.FallbackRelease,
                options.CacheDirectoryPath,
                options.DownloadProgress,
                cancellationToken).ConfigureAwait(false);

            if (fallbackResult.IsSuccess)
            {
                return fallbackResult;
            }
        }

        return primaryResult;
    }

    private async Task<WinPeResult<string>> DownloadReleaseAsync(
        PowerShell7Release release,
        string cacheDirectoryPath,
        IProgress<WinPeDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out Uri? sourceUri))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "The PowerShell 7 download URL is invalid.",
                release.DownloadUrl);
        }

        Directory.CreateDirectory(cacheDirectoryPath);
        string cachePath = Path.Combine(cacheDirectoryPath, release.AssetName);
        if (File.Exists(cachePath))
        {
            return WinPeResult<string>.Success(cachePath);
        }

        string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.download";
        try
        {
            progress?.Report(new WinPeDownloadProgress { Percent = 0, Status = $"Downloading PowerShell {release.Version}." });
            using HttpResponseMessage response = await _httpClient
                .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
            progress?.Report(new WinPeDownloadProgress { Percent = 100, Status = $"Downloaded PowerShell {release.Version}." });
            return WinPeResult<string>.Success(cachePath);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            TryDeleteFile(temporaryPath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.DownloadFailed,
                $"Failed to download PowerShell {release.Version}.",
                ex.Message);
        }
    }

    private async Task<WinPeResult> ConfigureEnvironmentAsync(
        WinPePowerShell7ProvisioningOptions options,
        CancellationToken cancellationToken)
    {
        string hivePath = Path.Combine(options.MountedImagePath, "Windows", "System32", "config", "SYSTEM");
        if (!File.Exists(hivePath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "The boot image SYSTEM registry hive was not found.",
                $"Expected path: '{hivePath}'.");
        }

        WinPeProcessExecution loadResult = await RunRegAsync(
            $"LOAD {HiveMountName} {WinPeProcessRunner.Quote(hivePath)}",
            options.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!loadResult.IsSuccess)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to load the boot image SYSTEM registry hive for PowerShell 7 setup.",
                loadResult.ToDiagnosticText());
        }

        try
        {
            string environmentKey = $@"{HiveMountName}\{EnvironmentKeyRelativePath}";

            string currentPath = await QueryEnvironmentValueAsync(environmentKey, "Path", options.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            string newPath = AppendPathEntry(currentPath, RuntimeInstallPath);

            string currentModulePath = await QueryEnvironmentValueAsync(environmentKey, "PSModulePath", options.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            string moduleBase = string.IsNullOrWhiteSpace(currentModulePath) ? DefaultPsModulePath : currentModulePath;
            string newModulePath = AppendPathEntry(moduleBase, $@"{RuntimeInstallPath}\Modules");

            WinPeResult pathResult = await SetEnvironmentValueAsync(environmentKey, "Path", newPath, options.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            if (!pathResult.IsSuccess)
            {
                return pathResult;
            }

            return await SetEnvironmentValueAsync(environmentKey, "PSModulePath", newModulePath, options.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await RunRegAsync($"UNLOAD {HiveMountName}", options.WorkingDirectoryPath, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<string> QueryEnvironmentValueAsync(
        string environmentKey,
        string valueName,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution queryResult = await RunRegAsync(
            $"QUERY {WinPeProcessRunner.Quote(environmentKey)} /v {valueName}",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        return queryResult.IsSuccess
            ? ParseRegValue(queryResult.StandardOutput, valueName)
            : string.Empty;
    }

    private async Task<WinPeResult> SetEnvironmentValueAsync(
        string environmentKey,
        string valueName,
        string value,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution addResult = await RunRegAsync(
            $"ADD {WinPeProcessRunner.Quote(environmentKey)} /v {valueName} /t REG_EXPAND_SZ /d {WinPeProcessRunner.Quote(value)} /f",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        return addResult.IsSuccess
            ? WinPeResult.Success()
            : WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                $"Failed to set the '{valueName}' environment value for PowerShell 7.",
                addResult.ToDiagnosticText());
    }

    private Task<WinPeProcessExecution> RunRegAsync(string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync("reg.exe", arguments, workingDirectory, cancellationToken);
    }

    private static string ParseRegValue(string queryOutput, string valueName)
    {
        foreach (string line in queryOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(valueName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int typeIndex = trimmed.IndexOf("REG_", StringComparison.OrdinalIgnoreCase);
            if (typeIndex < 0)
            {
                continue;
            }

            int dataIndex = trimmed.IndexOf("    ", typeIndex, StringComparison.Ordinal);
            if (dataIndex < 0)
            {
                dataIndex = trimmed.IndexOf('\t', typeIndex);
            }

            return dataIndex >= 0 ? trimmed[dataIndex..].Trim() : string.Empty;
        }

        return string.Empty;
    }

    private static string AppendPathEntry(string current, string entry)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return entry;
        }

        string[] parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            return current;
        }

        return $"{current.TrimEnd(';')};{entry}";
    }

    private static void TryCopyIcuDependency(string mountedImagePath)
    {
        try
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sourceIcuPath = Path.Combine(windowsDirectory, "System32", "icu.dll");
            if (!File.Exists(sourceIcuPath))
            {
                return;
            }

            string destinationIcuPath = Path.Combine(mountedImagePath, "Windows", "System32", "icu.dll");
            if (!File.Exists(destinationIcuPath))
            {
                File.Copy(sourceIcuPath, destinationIcuPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: pwsh may still start via invariant globalization if ICU is unavailable.
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

    private static WinPeDiagnostic? ValidateOptions(WinPePowerShell7ProvisioningOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MountedImagePath) || !Directory.Exists(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path does not exist.",
                $"Path: '{options.MountedImagePath}'.");
        }

        if (options.Release is null || string.IsNullOrWhiteSpace(options.Release.DownloadUrl) || string.IsNullOrWhiteSpace(options.Release.AssetName))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "A PowerShell 7 release with a download URL is required.",
                "Set WinPePowerShell7ProvisioningOptions.Release.");
        }

        if (string.IsNullOrWhiteSpace(options.CacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "A cache directory path is required.",
                "Set WinPePowerShell7ProvisioningOptions.CacheDirectoryPath.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "A working directory path is required.",
                "Set WinPePowerShell7ProvisioningOptions.WorkingDirectoryPath.");
        }

        return null;
    }
}

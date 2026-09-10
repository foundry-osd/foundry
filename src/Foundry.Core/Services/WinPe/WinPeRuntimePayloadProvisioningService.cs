// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text.Json;
using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeRuntimePayloadProvisioningService : IWinPeRuntimePayloadProvisioningService
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/foundry-osd/foundry/releases/latest";

    private readonly IWinPeProcessRunner _processRunner;
    private readonly HttpClient _httpClient;

    public WinPeRuntimePayloadProvisioningService()
        : this(new WinPeProcessRunner(), CreateHttpClient())
    {
    }

    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner)
        : this(processRunner, CreateHttpClient())
    {
    }

    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner, HttpClient httpClient)
    {
        _processRunner = processRunner;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WinPeDiagnostic? validationError = ValidateOptions(options, requireDestination: false);
        if (validationError is not null)
        {
            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Failure(validationError);
        }

        try
        {
            string runtimeIdentifier = options.Architecture.ToDotnetRuntimeIdentifier();
            options = options with
            {
                Bootstrap = await PrepareLocalApplicationAsync("Foundry.Bootstrap", options.Bootstrap, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false),
                Connect = await PrepareLocalApplicationAsync("Foundry.Connect", options.Connect, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false),
                Deploy = await PrepareLocalApplicationAsync("Foundry.Deploy", options.Deploy, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false)
            };
            var releaseApplications = new[]
            {
                (Name: "Foundry.Bootstrap", Options: options.Bootstrap),
                (Name: "Foundry.Connect", Options: options.Connect),
                (Name: "Foundry.Deploy", Options: options.Deploy)
            }.Where(application => application.Options.IsEnabled &&
                application.Options.ProvisioningSource == WinPeProvisioningSource.Release &&
                string.IsNullOrWhiteSpace(application.Options.ArchivePath)).ToArray();

            if (releaseApplications.Length > 0)
            {
                WinPeRuntimeReleaseSnapshot snapshot = options.ReleaseSnapshot ??
                    await GetReleaseSnapshotAsync(cancellationToken).ConfigureAwait(false);
                foreach (var application in releaseApplications)
                {
                    snapshot.GetAsset(ResolveReleaseAssetName(application.Name, runtimeIdentifier));
                }

                options = options with { ReleaseSnapshot = snapshot };
            }

            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Success(options);
        }
        catch (RuntimePublishException ex)
        {
            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Failure(ex.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or HttpRequestException or JsonException)
        {
            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Failure(
                WinPeErrorCodes.BuildFailed, "Failed to prepare Foundry runtime payloads.", ex.Message, exception: ex);
        }
    }

    private async Task<WinPeRuntimePayloadApplicationOptions> PrepareLocalApplicationAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions applicationOptions,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        if (!applicationOptions.IsEnabled ||
            (applicationOptions.ProvisioningSource == WinPeProvisioningSource.Release && string.IsNullOrWhiteSpace(applicationOptions.ArchivePath)))
        {
            return applicationOptions;
        }

        string archivePath = await ResolveArchivePathAsync(applicationName, applicationOptions,
            options.WorkingDirectoryPath, runtimeIdentifier, null, null, cancellationToken).ConfigureAwait(false);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.GetEntry($"{applicationName}.exe") is null)
        {
            throw new InvalidOperationException($"{applicationName} archive did not contain the expected executable '{applicationName}.exe'.");
        }

        return applicationOptions with { ArchivePath = archivePath };
    }

    public async Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeResult<WinPeRuntimePayloadProvisioningOptions> prepared = await PrepareAsync(options, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return WinPeResult.Failure(prepared.Error!);
        }

        options = prepared.Value!;
        try
        {
            string runtimeIdentifier = options.Architecture.ToDotnetRuntimeIdentifier();
            if (!string.IsNullOrWhiteSpace(options.MountedImagePath))
            {
                await ProvisionApplicationAsync("Foundry.Bootstrap", options.Bootstrap, options,
                    runtimeIdentifier, downloadProgress, cancellationToken).ConfigureAwait(false);
            }

            await ProvisionApplicationAsync(
                "Foundry.Connect",
                options.Connect,
                options,
                runtimeIdentifier,
                downloadProgress,
                cancellationToken).ConfigureAwait(false);

            await ProvisionApplicationAsync(
                "Foundry.Deploy",
                options.Deploy,
                options,
                runtimeIdentifier,
                downloadProgress,
                cancellationToken).ConfigureAwait(false);

            return WinPeResult.Success();
        }
        catch (RuntimePublishException ex)
        {
            return WinPeResult.Failure(ex.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or HttpRequestException or JsonException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry runtime payloads.",
                ex.Message,
                exception: ex);
        }
    }

    private async Task ProvisionApplicationAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions applicationOptions,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
    {
        if (!applicationOptions.IsEnabled)
        {
            return;
        }

        string archivePath = await ResolveArchivePathAsync(
            applicationName,
            applicationOptions,
            options.WorkingDirectoryPath,
            runtimeIdentifier,
            options.ReleaseSnapshot,
            downloadProgress,
            cancellationToken).ConfigureAwait(false);

        string extractionRoot = Path.Combine(
            options.WorkingDirectoryPath,
            "RuntimePayloads",
            applicationName,
            runtimeIdentifier);

        try
        {
            DirectoryOperations.Recreate(extractionRoot);
            ZipFile.ExtractToDirectory(archivePath, extractionRoot);
            string executablePath = Path.Combine(extractionRoot, $"{applicationName}.exe");
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException(
                    $"{applicationName} archive did not contain the expected executable '{applicationName}.exe'.");
            }

            foreach (string destinationRoot in ResolveDestinationRoots(applicationName, options, runtimeIdentifier))
            {
                CopyDirectory(extractionRoot, destinationRoot);
            }

            RemoveLegacyConnectSeed(applicationName, options);
        }
        finally
        {
            TryDeleteDirectory(extractionRoot);
        }
    }

    private async Task<string> ResolveArchivePathAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions options,
        string workingDirectoryPath,
        string runtimeIdentifier,
        WinPeRuntimeReleaseSnapshot? releaseSnapshot,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.ArchivePath))
        {
            string archivePath = Path.GetFullPath(options.ArchivePath);
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"{applicationName} archive was not found.", archivePath);
            }

            return archivePath;
        }

        if (options.ProvisioningSource == WinPeProvisioningSource.Release)
        {
            return await DownloadReleaseArchiveAsync(
                applicationName,
                workingDirectoryPath,
                runtimeIdentifier,
                releaseSnapshot!,
                downloadProgress,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            throw new ArgumentException($"{applicationName} project path or archive path is required when debug runtime provisioning is enabled.");
        }

        string projectPath = Path.GetFullPath(options.ProjectPath);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"{applicationName} project file was not found.", projectPath);
        }

        return await PublishProjectArchiveAsync(
            applicationName,
            projectPath,
            workingDirectoryPath,
            runtimeIdentifier,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PublishProjectArchiveAsync(
        string applicationName,
        string projectPath,
        string workingDirectoryPath,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        string debugWorkspace = Path.Combine(workingDirectoryPath, "DebugRuntime", applicationName);
        string publishDirectory = Path.Combine(debugWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(debugWorkspace, $"{applicationName}-{runtimeIdentifier}.zip");

        DirectoryOperations.Recreate(publishDirectory);
        Directory.CreateDirectory(debugWorkspace);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        string publishArguments = string.Join(
            " ",
            "publish",
            WinPeProcessRunner.Quote(projectPath),
            "-c", "Release",
            "-r", runtimeIdentifier,
            "--self-contained", "true",
            "/p:PublishSingleFile=true",
            "/p:EnableCompressionInSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:IncludeAllContentForSelfExtract=true",
            "/p:DebugType=None",
            "/p:GenerateDocumentationFile=false",
            "-o", WinPeProcessRunner.Quote(publishDirectory));

        WinPeProcessExecution publish = await _processRunner.RunAsync(
            "dotnet",
            publishArguments,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!publish.IsSuccess)
        {
            throw new RuntimePublishException(publish.ToFailureDiagnostic(
                WinPeErrorCodes.BuildFailed,
                $"Failed to publish {applicationName} runtime payload.",
                stage: "runtime.publish",
                toolName: "dotnet"));
        }

        string executablePath = Path.Combine(publishDirectory, $"{applicationName}.exe");
        if (!File.Exists(executablePath))
        {
            string message = $"{applicationName} publish output did not contain the expected executable '{executablePath}'.";
            throw new RuntimePublishException(new WinPeDiagnostic(
                WinPeErrorCodes.BuildFailed,
                message,
                publish.ToDiagnosticText(),
                stage: "runtime.publish",
                exitCode: publish.ExitCode,
                failureKind: WinPeFailureKinds.Process,
                failureReason: WinPeFailureReasons.ArtifactMissing,
                toolName: "dotnet"));
        }

        ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return archivePath;
    }

    private async Task<string> DownloadReleaseArchiveAsync(
        string applicationName,
        string workingDirectoryPath,
        string runtimeIdentifier,
        WinPeRuntimeReleaseSnapshot releaseSnapshot,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
    {
        string assetName = ResolveReleaseAssetName(applicationName, runtimeIdentifier);
        string releaseWorkspace = Path.Combine(workingDirectoryPath, "ReleaseRuntime", applicationName);
        Directory.CreateDirectory(releaseWorkspace);

        string archivePath = Path.Combine(releaseWorkspace, assetName);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        WinPeRuntimeReleaseAsset asset = releaseSnapshot.GetAsset(assetName);

        using HttpRequestMessage request = CreateGitHubRequest(asset.DownloadUrl);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string status = $"Downloading {applicationName} runtime payload.";
        ReportDownloadProgress(downloadProgress, 0, status);

        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream destination = File.Create(archivePath))
        {
            await CopyDownloadToFileAsync(
                source,
                destination,
                response.Content.Headers.ContentLength,
                status,
                downloadProgress,
                cancellationToken).ConfigureAwait(false);
        }

        await ValidateArchiveDigestAsync(archivePath, asset.Digest, cancellationToken).ConfigureAwait(false);
        return archivePath;
    }

    private async Task<WinPeRuntimeReleaseSnapshot> GetReleaseSnapshotAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateGitHubRequest(ReleaseApiUrl);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub release metadata did not contain an assets array.");
        }

        return new WinPeRuntimeReleaseSnapshot(ReadStringProperty(document.RootElement, "tag_name"),
            assets.EnumerateArray().Select(asset => new WinPeRuntimeReleaseAsset(
                ReadStringProperty(asset, "name"), ReadStringProperty(asset, "browser_download_url"), ReadStringProperty(asset, "digest"))));
    }

    private static string ResolveReleaseAssetName(string applicationName, string runtimeIdentifier)
    {
        return (applicationName, runtimeIdentifier) switch
        {
            ("Foundry.Bootstrap", "win-x64") => "Foundry.Bootstrap-win-x64.zip",
            ("Foundry.Bootstrap", "win-arm64") => "Foundry.Bootstrap-win-arm64.zip",
            ("Foundry.Connect", "win-x64") => "Foundry.Connect-win-x64.zip",
            ("Foundry.Connect", "win-arm64") => "Foundry.Connect-win-arm64.zip",
            ("Foundry.Deploy", "win-x64") => "Foundry.Deploy-win-x64.zip",
            ("Foundry.Deploy", "win-arm64") => "Foundry.Deploy-win-arm64.zip",
            _ => throw new InvalidOperationException($"No release asset mapping exists for {applicationName} and runtime '{runtimeIdentifier}'.")
        };
    }

    private static async Task ValidateArchiveDigestAsync(
        string archivePath,
        string digest,
        CancellationToken cancellationToken)
    {
        if (!TryReadSha256Digest(digest, out string expectedSha256))
        {
            return;
        }

        string actualSha256 = await FileHash.ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release archive digest mismatch. Expected SHA256 '{expectedSha256}', actual SHA256 '{actualSha256}'.");
        }
    }

    private static async Task CopyDownloadToFileAsync(
        Stream sourceStream,
        FileStream destinationStream,
        long? totalBytes,
        string status,
        IProgress<WinPeDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        int lastReportedPercent = -1;
        long bytesWritten = await StreamCopy.CopyAsync(
            sourceStream,
            destinationStream,
            copiedBytes =>
            {
                double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
                if (percentage.HasValue)
                {
                    int downloadPercent = (int)percentage.Value;
                    if (downloadPercent == lastReportedPercent)
                    {
                        return;
                    }

                    lastReportedPercent = downloadPercent;
                    ReportDownloadProgress(
                        progress,
                        downloadPercent,
                        $"{status} ({FormatBytes(copiedBytes)} / {FormatBytes(totalBytes.GetValueOrDefault())})");
                    return;
                }

                ReportDownloadProgress(
                    progress,
                    null,
                    $"{status} ({FormatBytes(copiedBytes)} downloaded)");
            },
            cancellationToken).ConfigureAwait(false);

        ReportDownloadProgress(
            progress,
            100,
            totalBytes is > 0
                ? $"{status} ({FormatBytes(bytesWritten)} / {FormatBytes(totalBytes.Value)})"
                : $"{status} ({FormatBytes(bytesWritten)} downloaded)");
    }

    private static void ReportDownloadProgress(IProgress<WinPeDownloadProgress>? progress, int? percent, string status)
    {
        progress?.Report(new WinPeDownloadProgress
        {
            Percent = percent.HasValue
                ? Math.Clamp(percent.Value, 0, 100)
                : null,
            Status = status
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static bool TryReadSha256Digest(string digest, out string sha256)
    {
        const string prefix = "sha256:";
        sha256 = string.Empty;

        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string value = digest[prefix.Length..].Trim();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        sha256 = value;
        return true;
    }

    private static string ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static HttpRequestMessage CreateGitHubRequest(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("FoundryOSD/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient();
    }

    private static IEnumerable<string> ResolveDestinationRoots(
        string applicationName,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier)
    {
        if (applicationName == "Foundry.Bootstrap")
        {
            if (!string.IsNullOrWhiteSpace(options.MountedImagePath))
            {
                yield return Path.Combine(Path.GetFullPath(options.MountedImagePath), "Foundry", "Bootstrap");
            }

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(options.MountedImagePath))
        {
            yield return Path.Combine(
                Path.GetFullPath(options.MountedImagePath),
                "Foundry",
                "Runtime",
                applicationName,
                runtimeIdentifier);
        }

        if (!string.IsNullOrWhiteSpace(options.UsbCacheRootPath))
        {
            yield return Path.Combine(
                Path.GetFullPath(options.UsbCacheRootPath),
                "Runtime",
                applicationName,
                runtimeIdentifier);
        }
    }

    private static void RemoveLegacyConnectSeed(
        string applicationName,
        WinPeRuntimePayloadProvisioningOptions options)
    {
        if (!applicationName.Equals("Foundry.Connect", StringComparison.Ordinal))
        {
            return;
        }

        foreach (string root in new[] { options.MountedImagePath, options.UsbCacheRootPath })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            TryDeleteFile(Path.Combine(root, "Foundry", "Seed", "Foundry.Connect.zip"));
        }
    }

    private static void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        DirectoryOperations.Recreate(destinationDirectoryPath);

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativeDirectoryPath = Path.GetRelativePath(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectoryPath, relativeDirectoryPath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativeFilePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
            string destinationFilePath = Path.Combine(destinationDirectoryPath, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.Copy(filePath, destinationFilePath, overwrite: true);
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeRuntimePayloadProvisioningOptions? options, bool requireDestination = true)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Runtime payload provisioning options are required.",
                "Provide a non-null WinPeRuntimePayloadProvisioningOptions instance.");
        }

        if (options.Bootstrap is null || options.Connect is null || options.Deploy is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Runtime payload application options are required.",
                "Provide non-null Bootstrap, Connect, and Deploy options.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Runtime payload working directory is required.",
                "Set WinPeRuntimePayloadProvisioningOptions.WorkingDirectoryPath.");
        }

        if (requireDestination && string.IsNullOrWhiteSpace(options.MountedImagePath) &&
            string.IsNullOrWhiteSpace(options.UsbCacheRootPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "At least one runtime payload destination is required.",
                "Set MountedImagePath, UsbCacheRootPath, or both.");
        }

        return null;
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
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private sealed class RuntimePublishException(WinPeDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public WinPeDiagnostic Diagnostic { get; } = diagnostic;
    }
}

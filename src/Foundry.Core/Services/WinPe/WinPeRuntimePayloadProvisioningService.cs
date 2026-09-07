// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;
using Foundry.Utilities.Networking;

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

    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner, HttpMessageHandler transport)
        : this(processRunner, CreateHttpClient(transport))
    {
    }
    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner, HttpClient httpClient)
    {
        _processRunner = processRunner;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        WinPeDiagnostic? validationError = ValidateOptions(options, requireDestination: true);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeResult<WinPePreparedRuntimePayloads> preparation = await PrepareAsync(options, downloadProgress, cancellationToken).ConfigureAwait(false);
        if (!preparation.IsSuccess)
        {
            return WinPeResult.Failure(preparation.Error!);
        }

        using WinPePreparedRuntimePayloads prepared = preparation.Value!;
        return await ProvisionPreparedAsync(prepared, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WinPeResult<WinPePreparedRuntimePayloads>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WinPeDiagnostic? validationError = ValidateOptions(options, requireDestination: false);
        if (validationError is not null)
        {
            return WinPeResult<WinPePreparedRuntimePayloads>.Failure(validationError);
        }

        string? ownedWorkspace = null;
        List<FileStream> readLeases = [];
        try
        {
            Guid mediaId = Guid.NewGuid();
            ownedWorkspace = Path.Combine(Path.GetFullPath(options.WorkingDirectoryPath), "RuntimePayloads", mediaId.ToString("N"));
            Directory.CreateDirectory(ownedWorkspace);
            string runtimeIdentifier = options.Architecture.ToDotnetRuntimeIdentifier();
            (string Name, WinPeRuntimePayloadApplicationOptions Options)[] selections =
            [
                ("Foundry.Connect", options.Connect),
                ("Foundry.Deploy", options.Deploy)
            ];
            var enabled = selections.Where(selection => selection.Options.IsEnabled).ToArray();
            HashSet<string> releaseAssetNames = enabled.Where(selection => selection.Options.ProvisioningSource == WinPeProvisioningSource.Release)
                .Select(selection => ResolveReleaseAssetName(selection.Name, runtimeIdentifier)).ToHashSet(StringComparer.Ordinal);
            Dictionary<string, ReleaseAsset> releaseAssets = releaseAssetNames.Count > 0
                ? await GetReleaseAssetsAsync(releaseAssetNames, cancellationToken).ConfigureAwait(false)
                : [];
            List<WinPePreparedRuntimeApplication> applications = [];
            foreach ((string applicationName, WinPeRuntimePayloadApplicationOptions applicationOptions) in enabled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseAsset? asset = null;
                if (applicationOptions.ProvisioningSource == WinPeProvisioningSource.Release)
                {
                    string assetName = ResolveReleaseAssetName(applicationName, runtimeIdentifier);
                    if (!releaseAssets.TryGetValue(assetName, out asset))
                    {
                        throw new InvalidDataException($"No release asset named '{assetName}' was found.");
                    }
                }

                string archivePath = await ResolveArchivePathAsync(applicationName, applicationOptions, ownedWorkspace,
                    runtimeIdentifier, asset, downloadProgress, cancellationToken).ConfigureAwait(false);
                using FileStream archiveLease = OpenReadLease(archivePath);
                string archiveSha256 = Convert.ToHexString(await SHA256.HashDataAsync(archiveLease, cancellationToken).ConfigureAwait(false));
                if (asset is not null && (archiveLease.Length != asset.SizeBytes ||
                    !string.Equals(archiveSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("The acquired runtime archive no longer matches the release metadata.");
                }

                string extractionRoot = Path.Combine(ownedWorkspace, applicationName, runtimeIdentifier);
                archiveLease.Position = 0;
                await ExtractArchiveAsync(archiveLease, extractionRoot, cancellationToken).ConfigureAwait(false);
                ValidateRuntimeBinaries(extractionRoot, applicationName, options.Architecture);
                List<WinPeRuntimeFile> files = [];
                foreach (string filePath in EnumerateRuntimeFiles(extractionRoot))
                {
                    FileStream lease = OpenReadLease(filePath);
                    readLeases.Add(lease);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(lease, cancellationToken).ConfigureAwait(false));
                    files.Add(new WinPeRuntimeFile(Path.GetRelativePath(extractionRoot, filePath), lease.Length, hash));
                }

                applications.Add(new WinPePreparedRuntimeApplication(applicationName, runtimeIdentifier, extractionRoot,
                    applicationOptions.ProvisioningSource, asset?.ReleaseTag, archiveSha256, files.AsReadOnly()));
            }

            var prepared = new WinPePreparedRuntimePayloads(mediaId, applications);
            prepared.TakeOwnership(ownedWorkspace, readLeases);
            ownedWorkspace = null;
            readLeases.Clear();
            return WinPeResult<WinPePreparedRuntimePayloads>.Success(prepared);
        }
        catch (Exception ex) when (IsProvisioningFailure(ex))
        {
            return WinPeResult<WinPePreparedRuntimePayloads>.Failure(
                WinPeErrorCodes.BuildFailed, "Failed to prepare Foundry runtime payloads.", ex.Message);
        }
        finally
        {
            foreach (FileStream lease in readLeases)
            {
                lease.Dispose();
            }

            if (ownedWorkspace is not null)
            {
                TryDeleteDirectory(ownedWorkspace);
            }
        }
    }

    /// <inheritdoc />
    public async Task<WinPeResult> ValidatePreparedAsync(
        WinPePreparedRuntimePayloads prepared,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ArgumentNullException.ThrowIfNull(prepared);
            ObjectDisposedException.ThrowIf(prepared.IsDisposed, prepared);
            HashSet<string> applications = new(StringComparer.Ordinal);
            foreach (WinPePreparedRuntimeApplication application in prepared.Applications)
            {
                if (application.ApplicationName is not ("Foundry.Connect" or "Foundry.Deploy") || !applications.Add(application.ApplicationName))
                {
                    throw new InvalidDataException("Prepared runtime application identity is invalid or duplicated.");
                }

                WinPeArchitecture architecture = application.RuntimeIdentifier switch
                {
                    "win-x64" => WinPeArchitecture.X64,
                    "win-arm64" => WinPeArchitecture.Arm64,
                    _ => throw new InvalidDataException("Prepared runtime architecture is invalid.")
                };
                HashSet<string> expectedPaths = new(StringComparer.OrdinalIgnoreCase);
                foreach (WinPeRuntimeFile file in application.Files)
                {
                    string path = ResolveRuntimeFilePath(application.DirectoryPath, file.RelativePath);
                    if (!expectedPaths.Add(path) || file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                    {
                        throw new InvalidDataException("Prepared runtime file identity is invalid or duplicated.");
                    }

                    await ValidateFileAsync(path, file, cancellationToken).ConfigureAwait(false);
                }

                if (!expectedPaths.SetEquals(EnumerateRuntimeFiles(application.DirectoryPath)))
                {
                    throw new InvalidDataException("The prepared runtime file set has changed.");
                }

                ValidateRuntimeBinaries(application.DirectoryPath, application.ApplicationName, architecture);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex) when (IsProvisioningFailure(ex))
        {
            return WinPeResult.Failure(WinPeErrorCodes.ValidationFailed, "Prepared runtime payload validation failed.", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WinPeResult> ProvisionPreparedAsync(
        WinPePreparedRuntimePayloads prepared,
        WinPeRuntimePayloadProvisioningOptions destinations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WinPeDiagnostic? validationError = ValidateOptions(destinations, requireDestination: true);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeResult validation = await ValidatePreparedAsync(prepared, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (prepared.Applications.Any(application => application.RuntimeIdentifier != destinations.Architecture.ToDotnetRuntimeIdentifier()))
        {
            return WinPeResult.Failure(WinPeErrorCodes.ValidationFailed, "Prepared runtime architecture does not match the destination.");
        }

        Dictionary<string, FileStream> sources = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            await AcquireVerifiedFilesAsync(prepared, sources, cancellationToken).ConfigureAwait(false);
            foreach (WinPePreparedRuntimeApplication application in prepared.Applications)
            {
                foreach (string destinationRoot in ResolveDestinationRoots(application.ApplicationName, destinations, application.RuntimeIdentifier))
                {
                    await CopyPreparedApplicationAsync(application, destinationRoot, sources, cancellationToken).ConfigureAwait(false);
                }

                RemoveLegacyConnectSeed(application.ApplicationName, destinations);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex) when (IsProvisioningFailure(ex))
        {
            return WinPeResult.Failure(WinPeErrorCodes.BuildFailed, "Failed to place prepared Foundry runtime payloads.", ex.Message);
        }
        finally
        {
            foreach (FileStream source in sources.Values)
            {
                source.Dispose();
            }
        }
    }

    private static bool IsProvisioningFailure(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            InvalidOperationException or HttpRequestException or JsonException or TimeoutException;

    private async Task<string> ResolveArchivePathAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions options,
        string workingDirectoryPath,
        string runtimeIdentifier,
        ReleaseAsset? releaseAsset,
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
                releaseAsset ?? throw new InvalidDataException("Release identity is required."),
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

        string[] publishArguments =
        [
            "publish",
            projectPath,
            "-c", "Release",
            "-r", runtimeIdentifier,
            "--self-contained", "true",
            "/p:PublishSingleFile=true",
            "/p:EnableCompressionInSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:IncludeAllContentForSelfExtract=true",
            "/p:DebugType=None",
            "/p:GenerateDocumentationFile=false",
            "-o", publishDirectory
        ];

        WinPeProcessExecution publish = await _processRunner.RunAsync(
            "dotnet",
            publishArguments,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!publish.IsSuccess)
        {
            throw new InvalidOperationException(publish.ToDiagnosticText());
        }

        string executablePath = Path.Combine(publishDirectory, $"{applicationName}.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                $"{applicationName} publish output did not contain the expected executable '{executablePath}'.");
        }

        ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return archivePath;
    }

    private async Task<string> DownloadReleaseArchiveAsync(
        string applicationName,
        string workingDirectoryPath,
        string runtimeIdentifier,
        ReleaseAsset asset,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
    {
        string archivePath = Path.Combine(workingDirectoryPath, "ReleaseRuntime", applicationName,
            ResolveReleaseAssetName(applicationName, runtimeIdentifier));
        string status = $"Downloading {applicationName} runtime payload.";
        ReportDownloadProgress(downloadProgress, 0, status);
        var progress = new DownloadByteProgress(downloadProgress, status, asset.SizeBytes);
        await ValidatedFileTransfer.DownloadAsync(_httpClient, new Uri(asset.DownloadUrl), archivePath,
            new FileIntegrity(new FileDigest(HashAlgorithmName.SHA256, asset.Sha256), asset.SizeBytes),
            new TransferLimits(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(30), asset.SizeBytes),
            progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        ReportDownloadProgress(downloadProgress, 100, $"{status} ({FormatBytes(asset.SizeBytes)} / {FormatBytes(asset.SizeBytes)})");
        return archivePath;
    }

    private async Task<Dictionary<string, ReleaseAsset>> GetReleaseAssetsAsync(IReadOnlySet<string> selectedAssetNames, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            using HttpRequestMessage request = CreateGitHubRequest(ReleaseApiUrl);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string metadata = await BoundedHttpContent.ReadStringAsync(response, 32 * 1024 * 1024, operation.Token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(metadata);
            if (!document.RootElement.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("GitHub release metadata did not contain an assets array.");
            }

            string releaseTag = ReadStringProperty(document.RootElement, "tag_name");
            if (string.IsNullOrWhiteSpace(releaseTag))
            {
                throw new InvalidDataException("GitHub release metadata did not contain a release tag.");
            }

            Dictionary<string, ReleaseAsset> result = new(StringComparer.Ordinal);
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = ReadStringProperty(asset, "name");
                if (!selectedAssetNames.Contains(name))
                {
                    continue;
                }

                string downloadUrl = ReadStringProperty(asset, "browser_download_url");
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    !TryReadSha256Digest(ReadStringProperty(asset, "digest"), out string sha256) ||
                    !asset.TryGetProperty("size", out JsonElement size) || size.ValueKind != JsonValueKind.Number ||
                    !size.TryGetInt64(out long sizeBytes) || sizeBytes <= 0)
                {
                    throw new InvalidDataException($"Release asset '{name}' requires an HTTPS URL, positive size, and valid SHA256 digest.");
                }

                if (!result.TryAdd(name, new ReleaseAsset(downloadUrl, sha256, sizeBytes, releaseTag)))
                {
                    throw new InvalidDataException($"Release asset '{name}' is duplicated.");
                }
            }

            return result;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new TimeoutException("Runtime release metadata acquisition timed out.", ex);
        }
    }

    private static string ResolveReleaseAssetName(string applicationName, string runtimeIdentifier)
    {
        return (applicationName, runtimeIdentifier) switch
        {
            ("Foundry.Connect", "win-x64") => "Foundry.Connect-win-x64.zip",
            ("Foundry.Connect", "win-arm64") => "Foundry.Connect-win-arm64.zip",
            ("Foundry.Deploy", "win-x64") => "Foundry.Deploy-win-x64.zip",
            ("Foundry.Deploy", "win-arm64") => "Foundry.Deploy-win-arm64.zip",
            _ => throw new InvalidOperationException($"No release asset mapping exists for {applicationName} and runtime '{runtimeIdentifier}'.")
        };
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

    private static HttpClient CreateHttpClient(HttpMessageHandler? transport = null)
    {
        return new HttpClient(new ValidatedRedirectHandler(
            transport ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false },
            static uri =>
            {
                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException("Runtime release requests require HTTPS at every redirect.");
                }
            }));
    }
    private static IEnumerable<string> ResolveDestinationRoots(
        string applicationName,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier)
    {
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

    private static async Task ExtractArchiveAsync(Stream archiveStream, string extractionRoot, CancellationToken cancellationToken)
    {
        const long maximumExtractedBytes = 8L * 1024 * 1024 * 1024;
        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            string relativePath = directory ? entry.FullName.TrimEnd('/', '\\') : entry.FullName;
            string target = ResolveRuntimeFilePath(extractionRoot, relativePath);
            if (!paths.Add(target) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("Runtime archive contains duplicate paths or links.");
            }

            if (entry.Length < 0 || entry.Length > maximumExtractedBytes - totalLength)
            {
                throw new InvalidDataException("Runtime archive exceeds the extraction size limit.");
            }

            totalLength += entry.Length;
        }

        Directory.CreateDirectory(extractionRoot);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            string target = ResolveRuntimeFilePath(extractionRoot, directory ? entry.FullName.TrimEnd('/', '\\') : entry.FullName);
            if (directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream source = entry.Open();
            await using FileStream destination = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long extractedBytes = await StreamCopy.CopyAsync(source, destination, copiedBytes =>
            {
                if (copiedBytes > entry.Length)
                {
                    throw new InvalidDataException("Runtime archive entry exceeds its declared length.");
                }
            }, cancellationToken).ConfigureAwait(false);
            if (extractedBytes != entry.Length)
            {
                throw new InvalidDataException("Extracted runtime file does not match its archive length.");
            }
        }
    }

    private static string ResolveRuntimeFilePath(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part =>
            string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException("Runtime payload contains an unsafe relative path.");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string path = Path.GetFullPath(Path.Combine(root, normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Runtime payload path leaves its staging directory.");
        }

        return path;
    }

    private static IReadOnlyList<string> EnumerateRuntimeFiles(string rootPath)
    {
        List<string> files = [];
        Stack<string> directories = new();
        directories.Push(Path.GetFullPath(rootPath));
        while (directories.TryPop(out string? directory))
        {
            RejectReparsePoint(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectReparsePoint(path);
                if (Directory.Exists(path))
                {
                    directories.Push(path);
                }
                else
                {
                    files.Add(path);
                }
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Runtime payload must not contain reparse points.");
        }
    }

    private static void ValidateRuntimeBinaries(string root, string applicationName, WinPeArchitecture architecture)
    {
        string apphost = Path.Combine(root, applicationName + ".exe");
        WinPeExecutableArchitecture.ValidateNative(apphost, architecture);
        foreach (string path in EnumerateRuntimeFiles(root))
        {
            string extension = Path.GetExtension(path);
            if ((extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) &&
                !path.Equals(apphost, StringComparison.OrdinalIgnoreCase))
            {
                WinPeExecutableArchitecture.ValidateRuntimeLibrary(path, architecture);
            }
        }
    }

    private static FileStream OpenReadLease(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task ValidateFileAsync(string path, WinPeRuntimeFile expected, CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        await using FileStream source = OpenReadLease(path);
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
        if (source.Length != expected.Length || !actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Prepared runtime file '{expected.RelativePath}' no longer matches its recorded bytes.");
        }
    }

    private static async Task AcquireVerifiedFilesAsync(
        WinPePreparedRuntimePayloads prepared, Dictionary<string, FileStream> sources, CancellationToken cancellationToken)
    {
        foreach (WinPePreparedRuntimeApplication application in prepared.Applications)
        {
            foreach (WinPeRuntimeFile file in application.Files)
            {
                string path = ResolveRuntimeFilePath(application.DirectoryPath, file.RelativePath);
                FileStream source = OpenReadLease(path);
                if (!sources.TryAdd(path, source))
                {
                    source.Dispose();
                    throw new InvalidDataException("Prepared applications must use distinct source files.");
                }

                string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false));
                if (source.Length != file.Length || !actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Prepared runtime file '{file.RelativePath}' changed before placement.");
                }
            }
        }
    }

    private static async Task CopyPreparedApplicationAsync(
        WinPePreparedRuntimeApplication application, string destinationRoot,
        IReadOnlyDictionary<string, FileStream> sources, CancellationToken cancellationToken)
    {
        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(application.DirectoryPath));
        string targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        if (sourceRoot.Equals(targetRoot, StringComparison.OrdinalIgnoreCase) ||
            targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            sourceRoot.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Runtime destination overlaps its prepared source.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        DirectoryOperations.Recreate(targetRoot);
        foreach (WinPeRuntimeFile file in application.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = ResolveRuntimeFilePath(sourceRoot, file.RelativePath);
            string destinationPath = ResolveRuntimeFilePath(targetRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            FileStream source = sources[sourcePath];
            source.Position = 0;
            await using (FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            await ValidateFileAsync(destinationPath, file, cancellationToken).ConfigureAwait(false);
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeRuntimePayloadProvisioningOptions? options, bool requireDestination)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Runtime payload provisioning options are required.",
                "Provide a non-null WinPeRuntimePayloadProvisioningOptions instance.");
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

        if (options.Connect is null || options.Deploy is null ||
            new[] { options.Connect, options.Deploy }.Any(application => application.IsEnabled &&
                (!Enum.IsDefined(application.ProvisioningSource) ||
                (application.ProvisioningSource == WinPeProvisioningSource.Release &&
                (!string.IsNullOrWhiteSpace(application.ArchivePath) || !string.IsNullOrWhiteSpace(application.ProjectPath))))))
        {
            return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed,
                "Runtime source options are invalid.", "Local archives and projects require an explicitly enabled debug source.");
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

    private sealed record ReleaseAsset(string DownloadUrl, string Sha256, long SizeBytes, string ReleaseTag);

    private sealed class DownloadByteProgress(IProgress<WinPeDownloadProgress>? progress, string status, long sizeBytes) : IProgress<long>
    {
        private int _lastPercent = -1;

        public void Report(long value)
        {
            int percent = (int)(TransferProgress.CalculatePercentage(value, sizeBytes) ?? 0);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                ReportDownloadProgress(progress, percent, $"{status} ({FormatBytes(value)} / {FormatBytes(sizeBytes)})");
            }
        }
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text.Json;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Models.Packages;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Runtime;
using Foundry.Utilities.IO;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Progress;
using Serilog;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Prepares the Foundry runtime archives of a media build and copies their files into the mounted boot image: Foundry.WinPE
/// into <c>Foundry\WinPE</c>, the Foundry.PostInstall launch files into <c>Foundry\PostInstall</c> and, on debug media
/// only, the Foundry.AutopilotInteractive archive into <c>Foundry\AutopilotInteractive</c>.
/// </summary>
public sealed class WinPeRuntimePayloadProvisioningService : IWinPeRuntimePayloadProvisioningService
{
    private const string WinPeApplicationName = WinPeRuntimePayloadProvisioningOptions.WinPeApplicationName;
    private const string PostInstallApplicationName = "Foundry.PostInstall";

    /// <summary>The only Foundry.PostInstall files copied into the boot image and staged by Deploy into the target OS.</summary>
    private static readonly string[] PostInstallLaunchFiles =
    [
        PostInstallRuntimeManifest.ExecutableFileName,
        PostInstallRuntimeManifest.LaunchScriptFileName,
        PostInstallRuntimeManifest.FileName
    ];

    private readonly IWinPeProcessRunner _processRunner;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string?> _environment;

    public WinPeRuntimePayloadProvisioningService()
        : this(new WinPeProcessRunner(), CreateHttpClient())
    {
    }

    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner)
        : this(processRunner, CreateHttpClient())
    {
    }

    /// <summary>Creates the service with injected dependencies.</summary>
    /// <param name="processRunner">Runs local debug publishes.</param>
    /// <param name="httpClient">Reads release metadata and downloads release archives.</param>
    /// <param name="environment">Reads the release override variables; the process environment when omitted.</param>
    internal WinPeRuntimePayloadProvisioningService(
        IWinPeProcessRunner processRunner,
        HttpClient httpClient,
        Func<string, string?>? environment = null)
    {
        _processRunner = processRunner;
        _httpClient = httpClient;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <inheritdoc />
    public Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        return PrepareCoreAsync(options, null, cancellationToken);
    }

    private async Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareCoreAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
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
                WinPe = await PrepareLocalApplicationAsync(WinPeApplicationName, options.WinPe, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false),
                PostInstall = await PrepareLocalApplicationAsync(PostInstallApplicationName, options.PostInstall, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false),
                AutopilotInteractive = await PrepareLocalApplicationAsync(AutopilotInteractiveLayout.ApplicationName, options.AutopilotInteractive, options, runtimeIdentifier, cancellationToken).ConfigureAwait(false)
            };
            var releaseApplications = new[]
            {
                (Name: WinPeApplicationName, Options: options.WinPe),
                (Name: PostInstallApplicationName, Options: options.PostInstall)
            }.Where(application => application.Options.IsEnabled &&
                application.Options.ProvisioningSource == WinPeProvisioningSource.Release &&
                string.IsNullOrWhiteSpace(application.Options.ArchivePath)).ToArray();

            if (releaseApplications.Length > 0)
            {
                WinPeRuntimeReleaseSnapshot snapshot = options.ReleaseSnapshot ??
                    await GetReleaseSnapshotAsync(options.ReleaseVersion, cancellationToken).ConfigureAwait(false);
                foreach (var application in releaseApplications)
                {
                    WinPeRuntimeReleaseAsset asset = snapshot.GetAsset(ResolveReleaseAssetName(application.Name, runtimeIdentifier));
                    if (!TryReadSha256Digest(asset.Digest, out _))
                    {
                        throw new InvalidDataException($"Release asset '{asset.Name}' requires a valid SHA256 digest.");
                    }
                }

                options = options with { ReleaseSnapshot = snapshot };
                foreach (var application in releaseApplications)
                {
                    WinPeRuntimePayloadApplicationOptions preparedApplication = await PrepareApplicationAsync(
                        application.Name, application.Options, options, runtimeIdentifier, downloadProgress, cancellationToken).ConfigureAwait(false);
                    options = application.Name == WinPeApplicationName
                        ? options with { WinPe = preparedApplication }
                        : options with { PostInstall = preparedApplication };
                }
            }

            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Success(options);
        }
        catch (RuntimePublishException ex)
        {
            return WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Failure(ex.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or HttpRequestException or JsonException or TimeoutException)
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
        if (!applicationOptions.IsEnabled)
        {
            return applicationOptions;
        }

        if (applicationName == AutopilotInteractiveLayout.ApplicationName &&
            string.IsNullOrWhiteSpace(applicationOptions.ArchivePath) &&
            (applicationOptions.ProvisioningSource == WinPeProvisioningSource.Release || string.IsNullOrWhiteSpace(applicationOptions.ProjectPath)))
        {
            // Release media never carry this app: Deploy downloads it into the target OS, so there is no release asset to fall back to.
            throw new InvalidOperationException($"{applicationName} is only provisioned on debug media, from a local archive or project.");
        }

        if (applicationOptions.ProvisioningSource == WinPeProvisioningSource.Release && string.IsNullOrWhiteSpace(applicationOptions.ArchivePath))
        {
            return applicationOptions;
        }

        return await PrepareApplicationAsync(applicationName, applicationOptions, options, runtimeIdentifier, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WinPeRuntimePayloadApplicationOptions> PrepareApplicationAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions applicationOptions,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken)
    {
        string expectedHash = applicationOptions.ArchiveSha256;
        if (applicationOptions.ProvisioningSource == WinPeProvisioningSource.Release && string.IsNullOrWhiteSpace(applicationOptions.ArchivePath))
        {
            WinPeRuntimeReleaseAsset asset = options.ReleaseSnapshot!.GetAsset(ResolveReleaseAssetName(applicationName, runtimeIdentifier));
            if (!TryReadSha256Digest(asset.Digest, out expectedHash))
            {
                throw new InvalidDataException($"Release asset '{asset.Name}' requires a valid SHA256 digest.");
            }
        }

        string archivePath = await ResolveArchivePathAsync(applicationName, applicationOptions,
            options.WorkingDirectoryPath, runtimeIdentifier, options.ReleaseSnapshot, downloadProgress, cancellationToken).ConfigureAwait(false);
        await using FileStream stream = File.OpenRead(archivePath);
        string hash = await ComputeArchiveHashAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(expectedHash) &&
            !string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Prepared {applicationName} archive SHA256 changed after preparation.");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.GetEntry($"{applicationName}.exe") is null)
        {
            throw new InvalidOperationException($"{applicationName} archive did not contain the expected executable '{applicationName}.exe'.");
        }

        if (applicationName == PostInstallApplicationName)
            await ValidatePostInstallArchiveAsync(archive, runtimeIdentifier, cancellationToken).ConfigureAwait(false);

        return applicationOptions with { ArchivePath = archivePath, ArchiveSha256 = hash };
    }

    /// <summary>
    /// Requires one of each launch file at the archive root and a supported manifest that lists only the executable and
    /// <c>Launch.cmd</c>, the files Foundry OSD copies into the boot image.
    /// </summary>
    private static async Task ValidatePostInstallArchiveAsync(ZipArchive archive, string runtimeIdentifier, CancellationToken cancellationToken)
    {
        foreach (string fileName in PostInstallLaunchFiles)
        {
            if (archive.Entries.Count(entry => entry.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidDataException($"PostInstall runtime archive must contain exactly one '{fileName}' at its root.");
        }

        PostInstallRuntimeManifest manifest = await ReadPostInstallManifestAsync(
            FindEntry(archive, PostInstallRuntimeManifest.FileName), cancellationToken).ConfigureAwait(false);
        if (manifest.SchemaVersion != 1 || manifest.ContractVersion != 1 || manifest.RuntimeIdentifier != runtimeIdentifier)
            throw new InvalidDataException("PostInstall runtime metadata is incompatible with the selected Windows image or deployment contract.");
        string[] listed = manifest.Files?.Select(file => file?.RelativePath ?? string.Empty).ToArray() ?? [];
        if (listed.Length != 2 ||
            !listed.Contains(PostInstallRuntimeManifest.ExecutableFileName, StringComparer.OrdinalIgnoreCase) ||
            !listed.Contains(PostInstallRuntimeManifest.LaunchScriptFileName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("PostInstall runtime metadata must list exactly the executable and Launch.cmd.");
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName) =>
        archive.Entries.Single(entry => entry.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private static async Task<PostInstallRuntimeManifest> ReadPostInstallManifestAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length is <= 0 or > 65536)
            throw new InvalidDataException("PostInstall runtime metadata is missing, ambiguous or exceeds its size limit.");
        await using Stream input = entry.Open();
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (bounded.Length + count > 65536) throw new InvalidDataException("PostInstall runtime metadata exceeds its size limit.");
            bounded.Write(buffer, 0, count);
        }

        try
        {
            return JsonSerializer.Deserialize<PostInstallRuntimeManifest>(bounded.ToArray(), Configuration.ConfigurationJsonDefaults.SerializerOptions)
                ?? throw new InvalidDataException("PostInstall runtime metadata is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("PostInstall runtime metadata is invalid.", exception);
        }
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

        WinPeResult<WinPeRuntimePayloadProvisioningOptions> prepared = await PrepareCoreAsync(options, downloadProgress, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return WinPeResult.Failure(prepared.Error!);
        }

        options = prepared.Value!;
        try
        {
            string foundryRoot = Path.Combine(Path.GetFullPath(options.MountedImagePath), "Foundry");
            await ProvisionApplicationAsync(WinPeApplicationName, options.WinPe, foundryRoot, cancellationToken).ConfigureAwait(false);
            await ProvisionApplicationAsync(PostInstallApplicationName, options.PostInstall, foundryRoot, cancellationToken).ConfigureAwait(false);
            await ProvisionApplicationAsync(AutopilotInteractiveLayout.ApplicationName, options.AutopilotInteractive, foundryRoot, cancellationToken).ConfigureAwait(false);
            RemoveLegacyPayloads(foundryRoot);
            return WinPeResult.Success();
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

    /// <summary>
    /// Copies one prepared archive into the mounted image after checking that its bytes still match the hash pinned during
    /// preparation. A disabled AutopilotInteractive leaves no directory, so release images never carry it.
    /// </summary>
    private static async Task ProvisionApplicationAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions applicationOptions,
        string foundryRoot,
        CancellationToken cancellationToken)
    {
        string autopilotInteractiveRoot = Path.Combine(foundryRoot, WinPeWorkspaceLayout.AutopilotInteractiveDirectoryName);
        if (!applicationOptions.IsEnabled)
        {
            if (applicationName == AutopilotInteractiveLayout.ApplicationName && Directory.Exists(autopilotInteractiveRoot))
                Directory.Delete(autopilotInteractiveRoot, recursive: true);
            return;
        }

        await using FileStream source = File.OpenRead(applicationOptions.ArchivePath);
        string hash = await ComputeArchiveHashAsync(source, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(hash, applicationOptions.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Prepared {applicationName} archive SHA256 changed before provisioning.");
        }

        switch (applicationName)
        {
            case WinPeApplicationName:
            {
                string destination = Path.Combine(foundryRoot, "WinPE");
                DirectoryOperations.Recreate(destination);
                using var archive = new ZipArchive(source, ZipArchiveMode.Read);
                archive.ExtractToDirectory(destination);
                break;
            }
            case PostInstallApplicationName:
            {
                string destination = Path.Combine(foundryRoot, WinPeWorkspaceLayout.PostInstallDirectoryName);
                DirectoryOperations.Recreate(destination);
                using var archive = new ZipArchive(source, ZipArchiveMode.Read);
                foreach (string fileName in PostInstallLaunchFiles)
                {
                    FindEntry(archive, fileName).ExtractToFile(Path.Combine(destination, fileName));
                }

                await VerifyPostInstallFilesAsync(destination, cancellationToken).ConfigureAwait(false);
                break;
            }
            default:
                DirectoryOperations.Recreate(autopilotInteractiveRoot);
                await using (FileStream destination = File.Create(Path.Combine(autopilotInteractiveRoot, WinPeWorkspaceLayout.AutopilotInteractiveArchiveFileName)))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }

    /// <summary>
    /// Checks the length and SHA256 of every file the extracted manifest lists, so media creation fails instead of Deploy
    /// rejecting the boot media before deployment.
    /// </summary>
    private static async Task VerifyPostInstallFilesAsync(string directory, CancellationToken cancellationToken)
    {
        PostInstallRuntimeManifest manifest = JsonSerializer.Deserialize<PostInstallRuntimeManifest>(
            await File.ReadAllBytesAsync(Path.Combine(directory, PostInstallRuntimeManifest.FileName), cancellationToken).ConfigureAwait(false),
            Configuration.ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("PostInstall runtime metadata is missing.");
        foreach (ContentPackageFile file in manifest.Files)
        {
            string path = Path.Combine(directory, file.RelativePath);
            if (new FileInfo(path).Length != file.Length ||
                !string.Equals(await FileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"PostInstall runtime file '{file.RelativePath}' does not match its manifest.");
            }
        }
    }

    private static async Task<string> ComputeArchiveHashAsync(FileStream stream, CancellationToken cancellationToken)
    {
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        string debugWorkspace = Path.Combine(workingDirectoryPath, "DebugRuntime", applicationName, Guid.NewGuid().ToString("N"));
        string publishDirectory = Path.Combine(debugWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(debugWorkspace, $"{applicationName}-{runtimeIdentifier}.zip");

        DirectoryOperations.Recreate(publishDirectory);
        Directory.CreateDirectory(debugWorkspace);
        // Project references may not inherit the RID; propagate Platform to keep every assembly on the target architecture.
        string platform = runtimeIdentifier == "win-arm64" ? "ARM64" : "x64";
        // Every runtime is a self-contained single-file executable, matching the release workflow. Foundry.WinPE and
        // Foundry.PostInstall ship in boot.wim: they extract nothing at startup (WPF's native libraries stay beside
        // Foundry.WinPE) and skip bundle compression because boot.wim already compresses them. Foundry.AutopilotInteractive
        // runs from its archive in the target OS, so it bundles and compresses everything.
        string[] singleFileProperties = applicationName != AutopilotInteractiveLayout.ApplicationName
            ?
            [
                "/p:EnableCompressionInSingleFile=false",
                "/p:IncludeNativeLibrariesForSelfExtract=false",
                "/p:IncludeAllContentForSelfExtract=false"
            ]
            :
            [
                "/p:EnableCompressionInSingleFile=true",
                "/p:IncludeNativeLibrariesForSelfExtract=true",
                "/p:IncludeAllContentForSelfExtract=true"
            ];
        string publishArguments = string.Join(
            " ",
            [
                "publish",
                WinPeProcessRunner.Quote(projectPath),
                "-c", "Release",
                "-r", runtimeIdentifier,
                $"/p:Platform={platform}",
                "--self-contained", "true",
                "/p:PublishSingleFile=true",
                .. singleFileProperties,
                "/p:DebugType=None",
                "/p:GenerateDocumentationFile=false",
                "-o", WinPeProcessRunner.Quote(publishDirectory)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        // Publishing writes into the workspace that cancellation will remove; wait for
        // the publisher to exit before allowing that cleanup or the next stage.
        WinPeProcessExecution publish = await _processRunner.RunAsync(
            "dotnet",
            publishArguments,
            workingDirectoryPath,
            CancellationToken.None).ConfigureAwait(false);

        if (!publish.IsSuccess)
        {
            throw new RuntimePublishException(publish.ToFailureDiagnostic(
                WinPeErrorCodes.BuildFailed,
                $"Failed to publish {applicationName} runtime payload.",
                stage: "runtime.publish",
                toolName: "dotnet"));
        }
        cancellationToken.ThrowIfCancellationRequested();

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

        if (applicationName == PostInstallApplicationName)
        {
            await CreatePostInstallArchiveAsync(publishDirectory, archivePath, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            return archivePath;
        }

        ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return archivePath;
    }

    /// <summary>
    /// Writes the runtime manifest of a local PostInstall publish and archives only the launch files, like the release
    /// workflow, so anything else the publish leaves beside the executable never reaches the boot image.
    /// </summary>
    private static async Task CreatePostInstallArchiveAsync(string publishDirectory, string archivePath, string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var files = new List<ContentPackageFile>();
        foreach (string name in new[] { PostInstallRuntimeManifest.ExecutableFileName, PostInstallRuntimeManifest.LaunchScriptFileName })
        {
            string path = Path.Combine(publishDirectory, name);
            files.Add(new()
            {
                RelativePath = name,
                Length = new FileInfo(path).Length,
                Sha256 = (await FileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false)).ToLowerInvariant()
            });
        }

        var manifest = new PostInstallRuntimeManifest { RuntimeIdentifier = runtimeIdentifier, Files = files };
        await File.WriteAllTextAsync(Path.Combine(publishDirectory, PostInstallRuntimeManifest.FileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken).ConfigureAwait(false);
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (string name in PostInstallLaunchFiles)
        {
            archive.CreateEntryFromFile(Path.Combine(publishDirectory, name), name, CompressionLevel.Optimal);
        }
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
        string releaseWorkspace = Path.Combine(workingDirectoryPath, "ReleaseRuntime", applicationName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(releaseWorkspace);

        string archivePath = Path.Combine(releaseWorkspace, assetName);
        WinPeRuntimeReleaseAsset asset = releaseSnapshot.GetAsset(assetName);

        string downloadedSha256 = await HttpTransfer.RunAsync(async (transferToken, reportProgress) =>
        {
            using HttpRequestMessage request = CreateGitHubRequest(asset.DownloadUrl);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                transferToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var item = new WinPeDownloadProgress
            {
                Status = WinPeProgressStatuses.DownloadingRuntimePayload,
                ItemName = applicationName
            };
            WinPeDownloadReporting.Report(downloadProgress, item, 0);

            await using Stream source = await response.Content.ReadAsStreamAsync(transferToken).ConfigureAwait(false);
            await using FileStream destination = File.Create(archivePath);
            WinPeDownloadedFile downloaded = await WinPeDownloadReporting.CopyAsync(
                source,
                destination,
                response.Content.Headers.ContentLength,
                item,
                downloadProgress,
                reportProgress,
                transferToken).ConfigureAwait(false);
            return downloaded.Sha256;
        }, cancellationToken).ConfigureAwait(false);

        // Checked against the SHA-256 computed during the download, so the archive is not read again.
        ValidateArchiveDigest(downloadedSha256, asset.Digest);
        return archivePath;
    }

    /// <summary>
    /// Reads the release of the Foundry OSD version creating the media, so the media carry the runtimes of that version.
    /// Only an unpublished local build reads the latest release.
    /// </summary>
    private async Task<WinPeRuntimeReleaseSnapshot> GetReleaseSnapshotAsync(string releaseVersion, CancellationToken cancellationToken)
    {
        FoundryReleaseSelection release = FoundryReleaseTag.Resolve(releaseVersion, _environment, FoundryReleaseTag.ReleaseTagVariable);
        if (release.IsLatest)
        {
            Log.ForContext<WinPeRuntimePayloadProvisioningService>().Warning(
                "Unpublished Foundry OSD build; using the latest release for boot media runtimes. ReleaseVersion={ReleaseVersion}",
                releaseVersion);
        }

        using HttpRequestMessage request = CreateGitHubRequest(release.ApiUrl);
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
            (WinPeApplicationName, "win-x64") => "Foundry.WinPE-win-x64.zip",
            (WinPeApplicationName, "win-arm64") => "Foundry.WinPE-win-arm64.zip",
            ("Foundry.PostInstall", "win-x64") => "Foundry.PostInstall-win-x64.zip",
            ("Foundry.PostInstall", "win-arm64") => "Foundry.PostInstall-win-arm64.zip",
            _ => throw new InvalidOperationException($"No release asset mapping exists for {applicationName} and runtime '{runtimeIdentifier}'.")
        };
    }

    private static void ValidateArchiveDigest(string actualSha256, string digest)
    {
        if (!TryReadSha256Digest(digest, out string expectedSha256))
        {
            throw new InvalidDataException("Release archive requires a valid SHA256 digest.");
        }

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release archive digest mismatch. Expected SHA256 '{expectedSha256}', actual SHA256 '{actualSha256}'.");
        }
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

        string value = digest[prefix.Length..];
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

    /// <summary>
    /// Removes payload layouts that earlier media builds staged in the boot image: the Bootstrap folder, the Runtime folder
    /// with its Connect, Deploy, PostInstall and AutopilotInteractive archives, the runtime trust manifest and the Connect
    /// seed archive.
    /// </summary>
    private static void RemoveLegacyPayloads(string foundryRoot)
    {
        TryDeleteDirectory(Path.Combine(foundryRoot, "Bootstrap"));
        TryDeleteDirectory(Path.Combine(foundryRoot, "Runtime"));
        TryDeleteFile(Path.Combine(foundryRoot, "Config", "foundry.runtime-trust.json"));
        TryDeleteFile(Path.Combine(foundryRoot, "Seed", "Foundry.Connect.zip"));
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

        if (options.WinPe is null || options.PostInstall is null || options.AutopilotInteractive is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Runtime payload application options are required.",
                "Provide non-null WinPe, PostInstall, and AutopilotInteractive options.");
        }

        // Foundry Deploy stages PostInstall from the boot image, so every media must carry it.
        if (!options.PostInstall.IsEnabled)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Foundry.PostInstall must be provisioned on every boot media.",
                "Set WinPeRuntimePayloadProvisioningOptions.PostInstall.IsEnabled to true; Foundry Deploy relies on it.");
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

        if (requireDestination && string.IsNullOrWhiteSpace(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "The mounted boot image is required to provision runtime payloads.",
                "Set WinPeRuntimePayloadProvisioningOptions.MountedImagePath.");
        }

        // An empty version must not fall back to the latest release: only the unpublished local version selects it.
        if (options.ReleaseSnapshot is null && string.IsNullOrWhiteSpace(options.ReleaseVersion) &&
            new[] { options.WinPe, options.PostInstall }.Any(application =>
                application.IsEnabled && application.ProvisioningSource == WinPeProvisioningSource.Release &&
                string.IsNullOrWhiteSpace(application.ArchivePath)))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "The Foundry OSD release version is required to provision release runtimes.",
                "Set WinPeRuntimePayloadProvisioningOptions.ReleaseVersion.");
        }

        return null;
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

    private sealed class RuntimePublishException(WinPeDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public WinPeDiagnostic Diagnostic { get; } = diagnostic;
    }
}

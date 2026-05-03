using System.IO.Compression;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeRuntimePayloadProvisioningService : IWinPeRuntimePayloadProvisioningService
{
    private readonly IWinPeProcessRunner _processRunner;

    public WinPeRuntimePayloadProvisioningService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeRuntimePayloadProvisioningService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        try
        {
            string runtimeIdentifier = options.Architecture.ToDotnetRuntimeIdentifier();

            await ProvisionApplicationAsync(
                "Foundry.Connect",
                options.Connect,
                options,
                runtimeIdentifier,
                cancellationToken).ConfigureAwait(false);

            await ProvisionApplicationAsync(
                "Foundry.Deploy",
                options.Deploy,
                options,
                runtimeIdentifier,
                cancellationToken).ConfigureAwait(false);

            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry runtime payloads.",
                ex.Message);
        }
    }

    private async Task ProvisionApplicationAsync(
        string applicationName,
        WinPeRuntimePayloadApplicationOptions applicationOptions,
        WinPeRuntimePayloadProvisioningOptions options,
        string runtimeIdentifier,
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
            cancellationToken).ConfigureAwait(false);

        string extractionRoot = Path.Combine(
            options.WorkingDirectoryPath,
            "RuntimePayloads",
            applicationName,
            runtimeIdentifier);

        try
        {
            WinPeFileSystemHelper.EnsureDirectoryClean(extractionRoot);
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

        if (string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            throw new ArgumentException($"{applicationName} project path or archive path is required when local runtime provisioning is enabled.");
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
        string localWorkspace = Path.Combine(workingDirectoryPath, "LocalRuntime", applicationName);
        string publishDirectory = Path.Combine(localWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(localWorkspace, $"{applicationName}-{runtimeIdentifier}.zip");

        WinPeFileSystemHelper.EnsureDirectoryClean(publishDirectory);
        Directory.CreateDirectory(localWorkspace);
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

    private static void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        WinPeFileSystemHelper.EnsureDirectoryClean(destinationDirectoryPath);

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

    private static WinPeDiagnostic? ValidateOptions(WinPeRuntimePayloadProvisioningOptions? options)
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

        if (string.IsNullOrWhiteSpace(options.MountedImagePath) &&
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
}

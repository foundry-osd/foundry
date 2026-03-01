using System.IO;
using System.Text.RegularExpressions;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

public sealed class WindowsDeploymentService : IWindowsDeploymentService
{
    private const int EfiPartitionSizeMb = 260;
    private const int MsrPartitionSizeMb = 16;
    private const int RecoveryPartitionSizeMb = 2048;
    private const string RecoveryPartitionLabel = "Recovery";
    private const string RecoveryPartitionGuid = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string RecoveryPartitionAttributes = "0x8000000000000001";
    private const string WinReImageFileName = "winre.wim";

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsDeploymentService> _logger;

    public WindowsDeploymentService(IProcessRunner processRunner, ILogger<WindowsDeploymentService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        int diskNumber,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (diskNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diskNumber), "Target disk number must be 0 or greater.");
        }

        _logger.LogInformation(
            "Preparing target disk layout. DiskNumber={DiskNumber}, RecoveryPartitionSizeMb={RecoveryPartitionSizeMb}, WorkingDirectory={WorkingDirectory}",
            diskNumber,
            RecoveryPartitionSizeMb,
            workingDirectory);
        (char systemLetter, char windowsLetter, char recoveryLetter) = GetPartitionLetters();
        Directory.CreateDirectory(workingDirectory);

        string[] scriptLines =
        [
            $"select disk {diskNumber}",
            "online disk noerr",
            "attributes disk clear readonly noerr",
            "clean",
            "convert gpt",
            $"create partition efi size={EfiPartitionSizeMb}",
            "format quick fs=fat32 label=System",
            $"assign letter={systemLetter}",
            $"create partition msr size={MsrPartitionSizeMb}",
            "create partition primary",
            "format quick fs=ntfs label=Windows",
            $"assign letter={windowsLetter}",
            $"select volume {windowsLetter}",
            $"shrink desired={RecoveryPartitionSizeMb} minimum={RecoveryPartitionSizeMb}",
            $"create partition primary size={RecoveryPartitionSizeMb}",
            $"set id=\"{RecoveryPartitionGuid}\"",
            $"gpt attributes={RecoveryPartitionAttributes}",
            $"format quick fs=ntfs label={RecoveryPartitionLabel}",
            $"assign letter={recoveryLetter}"
        ];

        string scriptPath = Path.Combine(workingDirectory, "diskpart-os-target.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);

        await RunRequiredProcessAsync(
            "diskpart.exe",
            $"/s \"{scriptPath}\"",
            workingDirectory,
            $"Disk partitioning failed for disk {diskNumber}",
            cancellationToken).ConfigureAwait(false);

        string systemPartitionRoot = $"{systemLetter}:\\";
        string windowsPartitionRoot = $"{windowsLetter}:\\";
        string recoveryPartitionRoot = $"{recoveryLetter}:\\";

        _logger.LogInformation(
            "Target disk layout prepared. DiskNumber={DiskNumber}, SystemPartition={SystemPartition}, WindowsPartition={WindowsPartition}, RecoveryPartition={RecoveryPartition}",
            diskNumber,
            systemPartitionRoot,
            windowsPartitionRoot,
            recoveryPartitionRoot);

        return new DeploymentTargetLayout
        {
            DiskNumber = diskNumber,
            SystemPartitionRoot = systemPartitionRoot,
            WindowsPartitionRoot = windowsPartitionRoot,
            RecoveryPartitionRoot = recoveryPartitionRoot,
            RecoveryPartitionLetter = recoveryLetter
        };
    }

    public async Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Operating system image was not found.", imagePath);
        }

        _logger.LogInformation("Resolving OS image index. ImagePath={ImagePath}, RequestedEdition={RequestedEdition}", imagePath, requestedEdition);
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "dism.exe",
                $"/English /Get-ImageInfo /ImageFile:\"{imagePath}\"",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Failed to resolve OS image index for {ImagePath}. Diagnostic={Diagnostic}", imagePath, ToDiagnostic(execution));
            throw new InvalidOperationException(
                $"Unable to resolve image index for '{imagePath}'.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        IReadOnlyList<ImageIndexDescriptor> descriptors = ParseImageDescriptors(execution.StandardOutput);
        if (descriptors.Count == 0)
        {
            return 1;
        }

        if (descriptors.Count == 1)
        {
            return descriptors[0].Index;
        }

        string requested = NormalizeToken(requestedEdition);
        if (requested.Length == 0)
        {
            return descriptors[0].Index;
        }

        ImageIndexDescriptor? bestMatch = descriptors.FirstOrDefault(item =>
            ContainsNormalized(item.Name, requested) ||
            ContainsNormalized(item.Edition, requested) ||
            ContainsNormalized(item.EditionId, requested));

        int resolvedIndex = bestMatch?.Index ?? descriptors[0].Index;
        _logger.LogInformation("Resolved OS image index {ImageIndex} for ImagePath={ImagePath}", resolvedIndex, imagePath);
        return resolvedIndex;
    }

    public async Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        _logger.LogInformation("Applying OS image. ImagePath={ImagePath}, Index={ImageIndex}, WindowsPartitionRoot={WindowsPartitionRoot}",
            imagePath,
            imageIndex,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        string[] arguments =
        [
            "/Apply-Image",
            $"/ImageFile:{imagePath}",
            $"/Index:{imageIndex}",
            $"/ApplyDir:{windowsPartitionRoot}",
            "/CheckIntegrity",
            $"/ScratchDir:{scratchDirectory}"
        ];

        if (progress is null)
        {
            await RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DismProgressReporter progressReporter = new(progress);
            await RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken,
                progressReporter.HandleOutput,
                progressReporter.HandleOutput).ConfigureAwait(false);
        }

        _logger.LogInformation("OS image apply completed. ImagePath={ImagePath}, Index={ImageIndex}", imagePath, imageIndex);
    }

    public async Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        string[] arguments =
        [
            "/English",
            $"/Image:{windowsPartitionRoot}",
            "/Get-CurrentEdition"
        ];

        ProcessExecutionResult execution = await RunRequiredProcessAsync(
            "dism.exe",
            arguments,
            workingDirectory,
            "Failed to query the applied Windows edition",
            cancellationToken).ConfigureAwait(false);

        Match editionMatch = Regex.Match(
            execution.StandardOutput,
            @"Current\s+Edition\s*:\s*(.+)",
            RegexOptions.IgnoreCase);

        if (!editionMatch.Success)
        {
            _logger.LogWarning("Unable to parse the applied Windows edition from DISM output.");
            return null;
        }

        string edition = editionMatch.Groups[1].Value.Trim();
        if (edition.Length == 0)
        {
            return null;
        }

        _logger.LogInformation("Detected applied Windows edition. Edition={Edition}", edition);
        return edition;
    }

    public async Task ConfigureRecoveryEnvironmentAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        string recoveryPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        if (string.IsNullOrWhiteSpace(systemPartitionRoot))
        {
            throw new ArgumentException("System partition root is required.", nameof(systemPartitionRoot));
        }

        Directory.CreateDirectory(workingDirectory);

        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        string sourceWinRePath = Path.Combine(windowsPath, "System32", "Recovery", WinReImageFileName);
        if (!File.Exists(sourceWinRePath))
        {
            throw new FileNotFoundException("The offline Windows image does not contain winre.wim.", sourceWinRePath);
        }

        string recoveryDirectory = GetRecoveryDirectoryPath(recoveryPartitionRoot);
        Directory.CreateDirectory(recoveryDirectory);

        string targetWinRePath = GetRecoveryImagePath(recoveryPartitionRoot);
        File.Copy(sourceWinRePath, targetWinRePath, overwrite: true);

        _logger.LogInformation(
            "Configuring recovery environment. WindowsPath={WindowsPath}, SystemPartitionRoot={SystemPartitionRoot}, RecoveryDirectory={RecoveryDirectory}",
            windowsPath,
            systemPartitionRoot,
            recoveryDirectory);

        string winReConfigToolPath = ResolveRequiredWinReConfigToolPath();
        string bcdStorePath = GetBcdStorePath(systemPartitionRoot);

        await RunRequiredProcessAsync(
            winReConfigToolPath,
            ["/setreimage", "/path", recoveryDirectory, "/target", windowsPath],
            workingDirectory,
            "Failed to set the Windows RE image location",
            cancellationToken).ConfigureAwait(false);

        string targetOsGuid = await ResolveTargetOsGuidAsync(
            bcdStorePath,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        await RunRequiredProcessAsync(
            winReConfigToolPath,
            ["/enable", "/osguid", targetOsGuid],
            workingDirectory,
            "Failed to enable Windows RE",
            cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult infoExecution = await RunRequiredProcessAsync(
            winReConfigToolPath,
            ["/info", "/target", windowsPath],
            workingDirectory,
            "Failed to query Windows RE status",
            cancellationToken).ConfigureAwait(false);

        ValidateRecoveryConfiguration(infoExecution.StandardOutput, recoveryDirectory);
        _logger.LogInformation("Recovery environment configured successfully.");
    }

    public async Task SealRecoveryPartitionAsync(
        string recoveryPartitionRoot,
        char recoveryPartitionLetter,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        char normalizedLetter = char.ToUpperInvariant(recoveryPartitionLetter);
        Directory.CreateDirectory(workingDirectory);

        string[] scriptLines =
        [
            $"select volume {normalizedLetter}",
            $"remove letter={normalizedLetter} noerr"
        ];

        string scriptPath = Path.Combine(workingDirectory, "diskpart-hide-recovery.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);

        await RunRequiredProcessAsync(
            "diskpart.exe",
            $"/s \"{scriptPath}\"",
            workingDirectory,
            "Failed to hide the recovery partition",
            cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(recoveryPartitionRoot))
        {
            throw new InvalidOperationException($"Recovery partition letter '{normalizedLetter}' is still accessible after sealing.");
        }

        _logger.LogInformation("Recovery partition sealed successfully. RecoveryPartitionLetter={RecoveryPartitionLetter}", normalizedLetter);
    }

    public async Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying offline drivers. DriverRoot={DriverRoot}, WindowsPartitionRoot={WindowsPartitionRoot}",
            driverRoot,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        await RunRequiredProcessAsync(
            "dism.exe",
            $"/Image:\"{windowsPartitionRoot}\" /Add-Driver /Driver:\"{driverRoot}\" /Recurse /ScratchDir:\"{scratchDirectory}\"",
            workingDirectory,
            $"Offline driver injection failed for '{driverRoot}'",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Offline driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    public async Task ApplyRecoveryDriversAsync(
        string recoveryPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        if (string.IsNullOrWhiteSpace(driverRoot))
        {
            throw new ArgumentException("Driver root is required.", nameof(driverRoot));
        }

        string winReImagePath = GetRecoveryImagePath(recoveryPartitionRoot);
        if (!File.Exists(winReImagePath))
        {
            throw new FileNotFoundException("The recovery partition does not contain winre.wim.", winReImagePath);
        }

        Directory.CreateDirectory(scratchDirectory);
        Directory.CreateDirectory(workingDirectory);

        string mountPath = Path.Combine(workingDirectory, "Mount-WindowsRE");
        ResetWorkingDirectory(mountPath);

        _logger.LogInformation(
            "Applying recovery drivers. DriverRoot={DriverRoot}, WinReImagePath={WinReImagePath}, MountPath={MountPath}",
            driverRoot,
            winReImagePath,
            mountPath);

        Exception? pendingException = null;
        bool mounted = false;
        bool shouldCommit = false;

        try
        {
            await RunRequiredProcessAsync(
                "dism.exe",
                $"/Mount-Image /ImageFile:\"{winReImagePath}\" /Index:1 /MountDir:\"{mountPath}\" /ScratchDir:\"{scratchDirectory}\"",
                workingDirectory,
                "Failed to mount the Windows RE image",
                cancellationToken).ConfigureAwait(false);

            mounted = true;

            await RunRequiredProcessAsync(
                "dism.exe",
                $"/Image:\"{mountPath}\" /Add-Driver /Driver:\"{driverRoot}\" /Recurse /ScratchDir:\"{scratchDirectory}\"",
                workingDirectory,
                $"Recovery driver injection failed for '{driverRoot}'",
                cancellationToken).ConfigureAwait(false);

            shouldCommit = true;
        }
        catch (Exception ex)
        {
            pendingException = ex;
        }
        finally
        {
            if (mounted)
            {
                string unmountArguments = shouldCommit
                    ? $"/Unmount-Image /MountDir:\"{mountPath}\" /Commit"
                    : $"/Unmount-Image /MountDir:\"{mountPath}\" /Discard";

                ProcessExecutionResult unmountExecution = await _processRunner
                    .RunAsync("dism.exe", unmountArguments, workingDirectory, cancellationToken)
                    .ConfigureAwait(false);

                if (!unmountExecution.IsSuccess)
                {
                    string diagnostic = ToDiagnostic(unmountExecution);
                    _logger.LogError("Failed to unmount the Windows RE image. Diagnostic={Diagnostic}", diagnostic);

                    pendingException = pendingException is null
                        ? new InvalidOperationException($"Failed to unmount the Windows RE image.{Environment.NewLine}{diagnostic}")
                        : new InvalidOperationException(
                            $"Windows RE servicing failed and the image could not be unmounted cleanly.{Environment.NewLine}{diagnostic}",
                            pendingException);
                }
            }

            TryDeleteDirectory(mountPath);
        }

        if (pendingException is not null)
        {
            throw pendingException;
        }

        _logger.LogInformation("Recovery driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    public async Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        int operatingSystemBuildMajor,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        _logger.LogInformation("Configuring boot files. WindowsPath={WindowsPath}, SystemPartitionRoot={SystemPartitionRoot}", windowsPath, systemPartitionRoot);

        string arguments = operatingSystemBuildMajor >= 26200
            ? $"\"{windowsPath}\" /s \"{systemPartitionRoot}\" /f UEFI /c /bootex"
            : $"\"{windowsPath}\" /s \"{systemPartitionRoot}\" /f UEFI /c /v";

        await RunRequiredProcessAsync(
            "bcdboot.exe",
            arguments,
            workingDirectory,
            "BCDBoot configuration failed",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("BCDBoot configuration completed successfully.");
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("{FailureSummary}. Diagnostic={Diagnostic}", failureSummary, ToDiagnostic(execution));
            throw new InvalidOperationException($"{failureSummary}.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        return execution;
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        return await RunRequiredProcessAsync(
            fileName,
            arguments,
            workingDirectory,
            failureSummary,
            cancellationToken,
            onOutputData: null,
            onErrorData: null).ConfigureAwait(false);
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken,
        Action<string>? onOutputData,
        Action<string>? onErrorData)
    {
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, onOutputData, onErrorData, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("{FailureSummary}. Diagnostic={Diagnostic}", failureSummary, ToDiagnostic(execution));
            throw new InvalidOperationException($"{failureSummary}.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        return execution;
    }

    private static (char systemLetter, char windowsLetter, char recoveryLetter) GetPartitionLetters()
    {
        HashSet<char> usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        char systemLetter = GetAvailableLetter(usedLetters, ['S', 'T', 'U', 'V', 'W']);
        usedLetters.Add(systemLetter);

        char windowsLetter = GetAvailableLetter(usedLetters, ['W', 'V', 'U', 'T', 'Q', 'P']);
        usedLetters.Add(windowsLetter);

        char recoveryLetter = GetAvailableLetter(usedLetters, ['R', 'X', 'Y', 'Z']);
        return (systemLetter, windowsLetter, recoveryLetter);
    }

    private static char GetAvailableLetter(HashSet<char> usedLetters, IReadOnlyList<char> preferred)
    {
        foreach (char preferredLetter in preferred)
        {
            char letter = char.ToUpperInvariant(preferredLetter);
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        for (char letter = 'D'; letter <= 'Z'; letter++)
        {
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        throw new InvalidOperationException("No drive letter is available for deployment partitions.");
    }

    private static void ResetWorkingDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup; a later DISM failure will surface if the mount path is unusable.
        }
    }

    private async Task<string> ResolveTargetOsGuidAsync(
        string bcdStorePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string bcdeditPath = ResolveRequiredSystemExecutablePath("bcdedit.exe");

        ProcessExecutionResult execution = await RunRequiredProcessAsync(
            bcdeditPath,
            ["/store", bcdStorePath, "/enum", "{default}", "/v"],
            workingDirectory,
            "Failed to enumerate the default target BCD entry",
            cancellationToken).ConfigureAwait(false);

        Match guidMatch = Regex.Match(
            execution.StandardOutput,
            @"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}");

        if (guidMatch.Success)
        {
            string targetOsGuid = guidMatch.Value;
            _logger.LogInformation("Resolved target OS BCD identifier {TargetOsGuid}", targetOsGuid);
            return targetOsGuid;
        }

        throw new InvalidOperationException(
            $"Unable to resolve the target Windows BCD identifier from '{bcdStorePath}'.");
    }

    private static string GetBcdStorePath(string systemPartitionRoot)
    {
        string gptBcdStorePath = Path.Combine(systemPartitionRoot, "EFI", "Microsoft", "Boot", "BCD");
        if (File.Exists(gptBcdStorePath))
        {
            return gptBcdStorePath;
        }

        string biosBcdStorePath = Path.Combine(systemPartitionRoot, "Boot", "BCD");
        if (File.Exists(biosBcdStorePath))
        {
            return biosBcdStorePath;
        }

        throw new FileNotFoundException("The target BCD store was not found on the system partition.", gptBcdStorePath);
    }

    private static string GetRecoveryDirectoryPath(string recoveryPartitionRoot)
    {
        return Path.Combine(recoveryPartitionRoot, "Recovery", "WindowsRE");
    }

    private static string GetRecoveryImagePath(string recoveryPartitionRoot)
    {
        return Path.Combine(GetRecoveryDirectoryPath(recoveryPartitionRoot), WinReImageFileName);
    }

    private static string ResolveRequiredWinReConfigToolPath()
    {
        string path = Path.Combine(Environment.SystemDirectory, "winrecfg.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Required WinPE executable 'winrecfg.exe' was not found. Add the WinPE-WinReCfg optional component to the WinPE image.",
                path);
        }

        return path;
    }

    private static string ResolveRequiredSystemExecutablePath(string executableName)
    {
        string path = Path.Combine(Environment.SystemDirectory, executableName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required system executable '{executableName}' was not found.", path);
        }

        return path;
    }

    private static void ValidateRecoveryConfiguration(string configurationOutput, string expectedRecoveryDirectory)
    {
        if (string.IsNullOrWhiteSpace(configurationOutput))
        {
            throw new InvalidOperationException("Windows RE status output is empty.");
        }

        string normalizedOutput = configurationOutput
            .Trim()
            .Replace('/', '\\');

        string normalizedExpectedLocation = expectedRecoveryDirectory
            .Trim()
            .Replace('/', '\\')
            .TrimEnd('\\');

        if (!normalizedOutput.Contains(normalizedExpectedLocation, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Windows RE is not mapped to the expected recovery directory '{normalizedExpectedLocation}'.{Environment.NewLine}{configurationOutput}");
        }
    }

    private static IReadOnlyList<ImageIndexDescriptor> ParseImageDescriptors(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var descriptors = new List<ImageIndexDescriptor>();
        ImageIndexDescriptor? current = null;

        foreach (string line in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            Match indexMatch = Regex.Match(line, @"^\s*Index\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase);
            if (indexMatch.Success)
            {
                if (current is not null)
                {
                    descriptors.Add(current);
                }

                current = new ImageIndexDescriptor
                {
                    Index = int.Parse(indexMatch.Groups[1].Value),
                    Name = string.Empty,
                    Edition = string.Empty,
                    EditionId = string.Empty
                };

                continue;
            }

            if (current is null)
            {
                continue;
            }

            Match nameMatch = Regex.Match(line, @"^\s*Name\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                current = current with { Name = nameMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionMatch = Regex.Match(line, @"^\s*Edition\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionMatch.Success)
            {
                current = current with { Edition = editionMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionIdMatch = Regex.Match(line, @"^\s*Edition\s+ID\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionIdMatch.Success)
            {
                current = current with { EditionId = editionIdMatch.Groups[1].Value.Trim() };
            }
        }

        if (current is not null)
        {
            descriptors.Add(current);
        }

        return descriptors;
    }

    private static bool ContainsNormalized(string source, string expected)
    {
        string normalized = NormalizeToken(source);
        if (normalized.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        return normalized.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
               expected.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] filtered = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(filtered);
    }

    private static string ToDiagnostic(ProcessExecutionResult execution)
    {
        return $"ExitCode={execution.ExitCode}{Environment.NewLine}" +
               $"StdOut:{Environment.NewLine}{execution.StandardOutput}{Environment.NewLine}" +
               $"StdErr:{Environment.NewLine}{execution.StandardError}";
    }

    private sealed record ImageIndexDescriptor
    {
        public required int Index { get; init; }
        public required string Name { get; init; }
        public required string Edition { get; init; }
        public required string EditionId { get; init; }
    }
}

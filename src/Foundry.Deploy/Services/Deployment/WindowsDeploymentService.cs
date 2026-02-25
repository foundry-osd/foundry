using System.IO;
using System.Text.RegularExpressions;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

public sealed class WindowsDeploymentService : IWindowsDeploymentService
{
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

        _logger.LogInformation("Preparing target disk layout. DiskNumber={DiskNumber}, WorkingDirectory={WorkingDirectory}", diskNumber, workingDirectory);
        (char systemLetter, char windowsLetter) = GetPartitionLetters();
        Directory.CreateDirectory(workingDirectory);

        string[] scriptLines =
        [
            $"select disk {diskNumber}",
            "online disk noerr",
            "attributes disk clear readonly noerr",
            "clean",
            "convert gpt",
            "create partition efi size=260",
            "format quick fs=fat32 label=System",
            $"assign letter={systemLetter}",
            "create partition msr size=16",
            "create partition primary",
            "format quick fs=ntfs label=Windows",
            $"assign letter={windowsLetter}"
        ];

        string scriptPath = Path.Combine(workingDirectory, "diskpart-os-target.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult execution = await _processRunner
            .RunAsync("diskpart.exe", $"/s \"{scriptPath}\"", workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Disk partitioning failed for disk {DiskNumber}. Diagnostic={Diagnostic}", diskNumber, ToDiagnostic(execution));
            throw new InvalidOperationException(
                $"Disk partitioning failed for disk {diskNumber}.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        _logger.LogInformation("Target disk layout prepared. DiskNumber={DiskNumber}, SystemPartition={SystemPartition}, WindowsPartition={WindowsPartition}",
            diskNumber,
            $"{systemLetter}:\\",
            $"{windowsLetter}:\\");
        return new DeploymentTargetLayout
        {
            DiskNumber = diskNumber,
            SystemPartitionRoot = $"{systemLetter}:\\",
            WindowsPartitionRoot = $"{windowsLetter}:\\"
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
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying OS image. ImagePath={ImagePath}, Index={ImageIndex}, WindowsPartitionRoot={WindowsPartitionRoot}",
            imagePath,
            imageIndex,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "dism.exe",
                $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{imageIndex} /ApplyDir:\"{windowsPartitionRoot}\" /CheckIntegrity /ScratchDir:\"{scratchDirectory}\"",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("OS image apply failed. ImagePath={ImagePath}, Index={ImageIndex}, Diagnostic={Diagnostic}",
                imagePath,
                imageIndex,
                ToDiagnostic(execution));
            throw new InvalidOperationException(
                $"OS image apply failed for index {imageIndex}.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        _logger.LogInformation("OS image apply completed. ImagePath={ImagePath}, Index={ImageIndex}", imagePath, imageIndex);
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

        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "dism.exe",
                $"/Image:\"{windowsPartitionRoot}\" /Add-Driver /Driver:\"{driverRoot}\" /Recurse /ScratchDir:\"{scratchDirectory}\"",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Offline driver injection failed. DriverRoot={DriverRoot}, Diagnostic={Diagnostic}", driverRoot, ToDiagnostic(execution));
            throw new InvalidOperationException(
                $"Offline driver injection failed for '{driverRoot}'.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        _logger.LogInformation("Offline driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    public async Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        _logger.LogInformation("Configuring boot files. WindowsPath={WindowsPath}, SystemPartitionRoot={SystemPartitionRoot}", windowsPath, systemPartitionRoot);
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "bcdboot.exe",
                $"\"{windowsPath}\" /s \"{systemPartitionRoot}\" /f UEFI",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("BCDBoot configuration failed. Diagnostic={Diagnostic}", ToDiagnostic(execution));
            throw new InvalidOperationException(
                $"BCDBoot configuration failed.{Environment.NewLine}{ToDiagnostic(execution)}");
        }

        _logger.LogInformation("BCDBoot configuration completed successfully.");
    }

    private static (char systemLetter, char windowsLetter) GetPartitionLetters()
    {
        HashSet<char> usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        char systemLetter = GetAvailableLetter(usedLetters, ['S', 'T', 'R', 'U', 'V', 'W']);
        usedLetters.Add(systemLetter);

        char windowsLetter = GetAvailableLetter(usedLetters, ['W', 'V', 'U', 'T', 'R', 'Q']);
        return (systemLetter, windowsLetter);
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
            .Where(ch => char.IsLetterOrDigit(ch))
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

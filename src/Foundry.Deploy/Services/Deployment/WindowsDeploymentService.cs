// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Hardware;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Prepares and validates the confirmed target disk layout.</summary>
public sealed class WindowsDeploymentService : IWindowsDeploymentService
{
    private static readonly TimeSpan MetadataExecutionTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NativeExecutionTimeout = TimeSpan.FromHours(4);
    private readonly Func<WindowsFirmwareType> _readFirmware;
    private readonly Action<DeploymentPartitionIdentity> _setRecoveryAttributes;
    private readonly WindowsDeploymentCommandRunner _commands;

    public WindowsDeploymentService(IProcessRunner processRunner, ILogger<WindowsDeploymentService> logger)
        : this(processRunner, logger, WindowsFirmwareInspector.GetCurrent, RecoveryPartitionAttributes.Apply) { }

    internal WindowsDeploymentService(IProcessRunner processRunner, ILogger<WindowsDeploymentService> logger,
        Func<WindowsFirmwareType> readFirmware, Action<DeploymentPartitionIdentity> setRecoveryAttributes)
    {
        _readFirmware = readFirmware;
        _setRecoveryAttributes = setRecoveryAttributes;
        _commands = new(processRunner, logger);
    }

    /// <inheritdoc />
    public async Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        TargetDiskIdentity expectedDisk,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDisk);
        if (_readFirmware() != WindowsFirmwareType.Uefi)
            throw new InvalidOperationException("Deployment requires UEFI boot mode.");
        if (expectedDisk.DiskNumber < 0 || expectedDisk.SizeBytes == 0 ||
            string.IsNullOrWhiteSpace(expectedDisk.BusType) ||
            (string.IsNullOrWhiteSpace(expectedDisk.UniqueId) && string.IsNullOrWhiteSpace(expectedDisk.SerialNumber)))
            throw new InvalidOperationException("The confirmed target disk identity is incomplete.");
        (char systemLetter, char windowsLetter, char recoveryLetter) = GetPartitionLetters();
        Directory.CreateDirectory(workingDirectory);
        ProcessExecutionResult result = await RunStorageScriptAsync(
            TargetDiskPreparationScript.Create(expectedDisk, systemLetter, windowsLetter, recoveryLetter),
            workingDirectory, cancellationToken, NativeExecutionTimeout).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        PreparedPartitions partitions = JsonSerializer.Deserialize<PreparedPartitions>(result.StandardOutput)
            ?? throw new InvalidOperationException("The prepared partition layout is unavailable.");
        partitions.System.Validate();
        partitions.Windows.Validate();
        partitions.Recovery.Validate();
        var layout = new DeploymentTargetLayout
        {
            DiskNumber = expectedDisk.DiskNumber,
            DiskIdentity = expectedDisk,
            SystemPartition = partitions.System,
            WindowsPartition = partitions.Windows,
            RecoveryPartition = partitions.Recovery,
            SystemPartitionRoot = partitions.System.VolumeRoot,
            WindowsPartitionRoot = partitions.Windows.VolumeRoot,
            RecoveryPartitionRoot = partitions.Recovery.VolumeRoot,
            RecoveryPartitionLetter = partitions.Recovery.DriveLetter
        };
        await RunStorageScriptAsync(TargetDiskPreparationScript.Validate(expectedDisk, partitions.Recovery),
            workingDirectory, cancellationToken).ConfigureAwait(false);
        _setRecoveryAttributes(partitions.Recovery);
        return layout;
    }

    private sealed record PreparedPartitions(DeploymentPartitionIdentity System, DeploymentPartitionIdentity Windows, DeploymentPartitionIdentity Recovery);

    private async Task<ProcessExecutionResult> RunStorageScriptAsync(string script, string workingDirectory, CancellationToken cancellationToken, TimeSpan? executionTimeout = null)
    {
        return await _commands.RunRequiredProcessAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(PowerShellCommand.CreateEncodedArguments(script)),
            workingDirectory, "The confirmed disk or partition operation failed", cancellationToken,
            executionTimeout ?? MetadataExecutionTimeout).ConfigureAwait(false);
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

}

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Hardware;

public sealed class TargetDiskService : ITargetDiskService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<TargetDiskService> _logger;

    public TargetDiskService(IProcessRunner processRunner, ILogger<TargetDiskService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Querying target disks.");
        ProcessExecutionResult execution = await RunDiskPartScriptAsync(
            ["list disk"],
            cancellationToken).ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogWarning("Target disk query returned no data. ExitCode={ExitCode}", execution.ExitCode);
            return [];
        }

        try
        {
            var disks = new List<TargetDiskInfo>();
            foreach (DiskPartDisk disk in DiskPartOutputParser.ParseListDisk(execution.StandardOutput))
            {
                TargetDiskInfo info = await QueryDiskInfoAsync(disk, cancellationToken).ConfigureAwait(false);
                if (!ShouldExcludeFromTargets(info))
                {
                    disks.Add(info);
                    continue;
                }

                _logger.LogInformation(
                    "Skipping disk {DiskNumber} from target selection because it is attached over USB. FriendlyName={FriendlyName}",
                    info.DiskNumber,
                    info.FriendlyName);
            }

            TargetDiskInfo[] orderedDisks = disks
                .OrderByDescending(disk => disk.IsSelectable)
                .ThenBy(disk => disk.DiskNumber)
                .ToArray();

            _logger.LogInformation("Resolved {DiskCount} target disks ({SelectableCount} selectable).",
                orderedDisks.Length,
                orderedDisks.Count(disk => disk.IsSelectable));
            return orderedDisks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse target disk query output.");
            return [];
        }
    }

    public async Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        string driveLetter = root.TrimEnd('\\').TrimEnd(':');
        if (driveLetter.Length == 0)
        {
            return null;
        }

        ProcessExecutionResult execution = await RunDiskPartScriptAsync(
            [
                $"select volume {driveLetter}",
                "detail volume"
            ],
            cancellationToken).ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogDebug("Disk number lookup for path {Path} returned no data. ExitCode={ExitCode}", path, execution.ExitCode);
            return null;
        }

        return DiskPartOutputParser.ParseDetailVolumeDiskNumber(execution.StandardOutput);
    }

    private async Task<TargetDiskInfo> QueryDiskInfoAsync(DiskPartDisk disk, CancellationToken cancellationToken)
    {
        ProcessExecutionResult execution = await RunDiskPartScriptAsync(
            [
                $"select disk {disk.Number}",
                "detail disk"
            ],
            cancellationToken).ConfigureAwait(false);

        DiskPartDetailDisk detail = execution.IsSuccess && !string.IsNullOrWhiteSpace(execution.StandardOutput)
            ? DiskPartOutputParser.ParseDetailDisk(disk.Number, execution.StandardOutput, disk)
            : new DiskPartDetailDisk(disk.Number, string.Empty, string.Empty, string.Empty, disk.IsGpt ? "GPT" : "MBR", disk.SizeBytes, false, false, false, disk.IsOffline);

        bool isRemovable = string.Equals(detail.BusType, "USB", StringComparison.OrdinalIgnoreCase);
        string warning = BuildSelectionWarning(detail.IsSystem, detail.IsBoot, detail.IsReadOnly, detail.IsOffline);

        return new TargetDiskInfo
        {
            DiskNumber = detail.Number,
            FriendlyName = NormalizeValue(detail.FriendlyName, fallback: LocalizationText.GetString("Common.Unknown")),
            SerialNumber = NormalizeValue(detail.SerialNumber, fallback: LocalizationText.GetString("Common.Unknown")),
            BusType = NormalizeValue(detail.BusType, fallback: LocalizationText.GetString("Common.Unknown")),
            PartitionStyle = NormalizeValue(detail.PartitionStyle, fallback: LocalizationText.GetString("Common.Unknown")),
            SizeBytes = detail.SizeBytes,
            IsSystem = detail.IsSystem,
            IsBoot = detail.IsBoot,
            IsReadOnly = detail.IsReadOnly,
            IsOffline = detail.IsOffline,
            IsRemovable = isRemovable,
            IsSelectable = string.IsNullOrWhiteSpace(warning),
            SelectionWarning = warning
        };
    }

    private static string BuildSelectionWarning(bool isSystem, bool isBoot, bool isReadOnly, bool isOffline)
    {
        if (isSystem)
        {
            return LocalizationText.GetString("Disk.BlockedSystemDisk");
        }

        if (isBoot)
        {
            return LocalizationText.GetString("Disk.BlockedBootDisk");
        }

        if (isReadOnly)
        {
            return LocalizationText.GetString("Disk.BlockedReadOnly");
        }

        if (isOffline)
        {
            return LocalizationText.GetString("Disk.BlockedOffline");
        }

        return string.Empty;
    }

    private static bool ShouldExcludeFromTargets(TargetDiskInfo disk)
        => string.Equals(disk.BusType, "USB", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeValue(string value, string fallback)
    {
        string normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private async Task<ProcessExecutionResult> RunDiskPartScriptAsync(
        IReadOnlyList<string> scriptLines,
        CancellationToken cancellationToken)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"foundry-diskpart-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);
            return await _processRunner
                .RunAsync("diskpart.exe", $"/s \"{scriptPath}\"", Path.GetTempPath(), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(scriptPath);
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
        }
    }
}

using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Models;
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
        string script = @"
$disks = Get-Disk | Sort-Object -Property Number
$result = foreach ($disk in $disks) {
    [pscustomobject]@{
        Number = [int]$disk.Number
        FriendlyName = [string]$disk.FriendlyName
        SerialNumber = [string]$disk.SerialNumber
        BusType = [string]$disk.BusType
        PartitionStyle = [string]$disk.PartitionStyle
        Size = [uint64]$disk.Size
        IsSystem = [bool]$disk.IsSystem
        IsBoot = [bool]$disk.IsBoot
        IsReadOnly = [bool]$disk.IsReadOnly
        IsOffline = [bool]$disk.IsOffline
        IsRemovable = [bool]$disk.IsRemovable
    }
}
$result | ConvertTo-Json -Compress
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, Path.GetTempPath(), cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogWarning("Target disk query returned no data. ExitCode={ExitCode}", execution.ExitCode);
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(execution.StandardOutput);
            JsonElement root = document.RootElement;

            var disks = new List<TargetDiskInfo>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement diskElement in root.EnumerateArray())
                {
                    TargetDiskInfo info = ParseDisk(diskElement);
                    if (ShouldExcludeFromTargets(info))
                    {
                        _logger.LogInformation(
                            "Skipping disk {DiskNumber} from target selection because it is attached over USB. FriendlyName={FriendlyName}",
                            info.DiskNumber,
                            info.FriendlyName);
                        continue;
                    }

                    disks.Add(info);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                TargetDiskInfo info = ParseDisk(root);
                if (!ShouldExcludeFromTargets(info))
                {
                    disks.Add(info);
                }
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

        string script = $@"
$partition = Get-Partition -DriveLetter '{EscapeForSingleQuote(driveLetter)}' -ErrorAction SilentlyContinue
if ($null -eq $partition) {{
    return
}}

[pscustomobject]@{{
    DiskNumber = [int]$partition.DiskNumber
}} | ConvertTo-Json -Compress
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, Path.GetTempPath(), cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogDebug("Disk number lookup for path {Path} returned no data. ExitCode={ExitCode}", path, execution.ExitCode);
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(execution.StandardOutput);
            JsonElement rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("DiskNumber", out JsonElement diskNumberElement))
            {
                return null;
            }

            if (diskNumberElement.ValueKind == JsonValueKind.Number && diskNumberElement.TryGetInt32(out int numericValue))
            {
                return numericValue;
            }

            if (diskNumberElement.ValueKind == JsonValueKind.String &&
                int.TryParse(diskNumberElement.GetString(), out int parsedValue))
            {
                return parsedValue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse disk number for path {Path}.", path);
            return null;
        }

        return null;
    }

    private static TargetDiskInfo ParseDisk(JsonElement element)
    {
        int diskNumber = ReadInt(element, "Number");
        string friendlyName = NormalizeValue(ReadString(element, "FriendlyName"), fallback: "Unknown");
        string serial = NormalizeValue(ReadString(element, "SerialNumber"), fallback: "Unknown");
        string busType = NormalizeValue(ReadString(element, "BusType"), fallback: "Unknown");
        string partitionStyle = NormalizeValue(ReadString(element, "PartitionStyle"), fallback: "Unknown");
        ulong sizeBytes = ReadUInt64(element, "Size");
        bool isSystem = ReadBool(element, "IsSystem");
        bool isBoot = ReadBool(element, "IsBoot");
        bool isReadOnly = ReadBool(element, "IsReadOnly");
        bool isOffline = ReadBool(element, "IsOffline");
        bool isRemovable = ReadBool(element, "IsRemovable");

        string warning = BuildSelectionWarning(isSystem, isBoot, isReadOnly, isOffline);

        return new TargetDiskInfo
        {
            DiskNumber = diskNumber,
            FriendlyName = friendlyName,
            SerialNumber = serial,
            BusType = busType,
            PartitionStyle = partitionStyle,
            SizeBytes = sizeBytes,
            IsSystem = isSystem,
            IsBoot = isBoot,
            IsReadOnly = isReadOnly,
            IsOffline = isOffline,
            IsRemovable = isRemovable,
            IsSelectable = string.IsNullOrWhiteSpace(warning),
            SelectionWarning = warning
        };
    }

    private static string BuildSelectionWarning(bool isSystem, bool isBoot, bool isReadOnly, bool isOffline)
    {
        if (isSystem)
        {
            return "Blocked: system disk";
        }

        if (isBoot)
        {
            return "Blocked: boot disk";
        }

        if (isReadOnly)
        {
            return "Blocked: read-only";
        }

        if (isOffline)
        {
            return "Blocked: offline";
        }

        return string.Empty;
    }

    private static bool ShouldExcludeFromTargets(TargetDiskInfo disk)
        => string.Equals(disk.BusType, "USB", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static int ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return -1;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int parsed))
        {
            return parsed;
        }

        return -1;
    }

    private static ulong ReadUInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt64(out ulong value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && ulong.TryParse(property.GetString(), out ulong parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out bool parsed))
        {
            return parsed;
        }

        return false;
    }

    private static string NormalizeValue(string value, string fallback)
    {
        string normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string EscapeForSingleQuote(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

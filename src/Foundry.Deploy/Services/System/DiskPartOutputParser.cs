using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.System;

internal static class DiskPartOutputParser
{
    public static IReadOnlyList<DiskPartDisk> ParseListDisk(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var disks = new List<DiskPartDisk>();
        foreach (string line in SplitLines(output))
        {
            Match match = Regex.Match(line, @"^\s*\*?\s*\D+?(?<number>\d+)\s+(?<status>[^\s-]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            disks.Add(new DiskPartDisk(
                int.Parse(match.Groups["number"].Value),
                ParseFirstSizeBytes(line),
                line.TrimEnd().EndsWith('*'),
                IsOfflineStatus(match.Groups["status"].Value)));
        }

        return disks;
    }

    public static IReadOnlyList<DiskPartPartition> ParseListPartition(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var partitions = new List<DiskPartPartition>();
        foreach (string line in SplitLines(output))
        {
            Match match = Regex.Match(line, @"^\s*\D+?(?<number>\d+)\s+(?<type>[^\s-]+)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                continue;
            }

            partitions.Add(new DiskPartPartition(
                int.Parse(match.Groups["number"].Value),
                match.Groups["type"].Value));
        }

        return partitions;
    }

    public static DiskPartDetailDisk ParseDetailDisk(int diskNumber, string output, DiskPartDisk disk)
    {
        string friendlyName = string.Empty;
        string serialNumber = string.Empty;
        string busType = string.Empty;
        string friendlyNameCandidate = string.Empty;
        var busInferenceLines = new List<string>();
        bool isBoot = false;
        bool isSystem = false;
        bool isReadOnly = false;
        bool isOffline = disk.IsOffline;

        foreach (string rawLine in SplitLines(output))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Contains(':', StringComparison.Ordinal) && friendlyName.Length == 0)
            {
                friendlyName = friendlyNameCandidate;
            }

            if (!line.Contains(':', StringComparison.Ordinal) && !IsDiskPartBoilerplateLine(line))
            {
                friendlyNameCandidate = line;
                continue;
            }

            if (IsBusInferenceLine(line))
            {
                busInferenceLines.Add(line);
            }

            if (TryReadKeyValue(line, @".*serial.*|.*s.rie.*", out string serial))
            {
                serialNumber = serial;
                continue;
            }

            if (TryReadKeyValue(line, @"type", out string type))
            {
                busType = type;
                continue;
            }

            if (TryReadKeyValue(line, @"status|statut", out string status))
            {
                isOffline = IsOfflineStatus(status);
                continue;
            }

            if (TryReadKeyValue(line, @".*read-only.*|.*lecture\s+seule.*", out string currentReadOnly))
            {
                isReadOnly = IsYes(currentReadOnly);
                continue;
            }

            if (TryReadKeyValue(line, @".*boot\s+disk.*|.*d.marrage.*", out string bootDisk))
            {
                isBoot = IsYes(bootDisk);
                continue;
            }

            if (TryReadKeyValue(line, @".*system\s+disk.*|.*syst.me.*", out string systemDisk))
            {
                isSystem = IsYes(systemDisk);
            }
        }

        if (string.IsNullOrWhiteSpace(busType))
        {
            busType = InferBusType(friendlyName, busInferenceLines);
        }

        return new DiskPartDetailDisk(
            diskNumber,
            friendlyName,
            serialNumber,
            busType,
            disk.IsGpt ? "GPT" : "MBR",
            disk.SizeBytes,
            isSystem,
            isBoot,
            isReadOnly,
            isOffline);
    }

    public static int? ParseDetailVolumeDiskNumber(string output)
    {
        foreach (string line in SplitLines(output))
        {
            Match match = Regex.Match(line, @"^\s*\*?\s*(?:Disk|Disque)\s+(?<number>\d+)\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return int.Parse(match.Groups["number"].Value);
            }
        }

        return null;
    }

    public static string ParseDetailPartitionTypeGuid(string output)
    {
        Match match = Regex.Match(
            output,
            @"(?<guid>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups["guid"].Value.ToLowerInvariant()
            : string.Empty;
    }

    public static char? ParseDetailPartitionDriveLetter(string output)
    {
        foreach (string line in SplitLines(output))
        {
            Match match = Regex.Match(line, @"^\s*(?:Volume)\s+\d+\s+(?<letter>[A-Z])\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return char.ToUpperInvariant(match.Groups["letter"].Value[0]);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SplitLines(string output)
        => output.Split(["\r\n", "\n"], StringSplitOptions.None);

    private static ulong ParseFirstSizeBytes(string line)
    {
        Match match = Regex.Match(
            line,
            @"(?<value>\d+)\s*(?<unit>KB|MB|GB|TB|K|M|G|T|B|octets)(?:\s*octets)?",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return 0;
        }

        ulong value = ulong.Parse(match.Groups["value"].Value);
        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" or "OCTETS" => value,
            "K" or "KB" => value * 1024UL,
            "M" or "MB" => value * 1024UL * 1024UL,
            "G" or "GB" => value * 1024UL * 1024UL * 1024UL,
            "T" or "TB" => value * 1024UL * 1024UL * 1024UL * 1024UL,
            _ => 0
        };
    }

    private static bool TryReadKeyValue(string line, string keyPattern, out string value)
    {
        value = string.Empty;
        Match match = Regex.Match(line, $"^(?:{keyPattern})\\s*:\\s*(?<value>.*)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return true;
    }

    private static bool IsDiskPartBoilerplateLine(string line)
    {
        return line.StartsWith("Microsoft DiskPart", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("DISKPART>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBusInferenceLine(string line)
    {
        if (!line.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return ResolveBusTypeFromText(line).Length > 0;
    }

    private static string InferBusType(string friendlyName, IReadOnlyList<string> inferenceLines)
    {
        return ResolveBusTypeFromText(string.Join('\n', inferenceLines.Prepend(friendlyName)));
    }

    private static string ResolveBusTypeFromText(string value)
    {
        string combined = value.ToUpperInvariant();
        if (combined.Contains("NVME", StringComparison.Ordinal))
        {
            return "NVMe";
        }

        if (combined.Contains("USB", StringComparison.Ordinal))
        {
            return "USB";
        }

        if (combined.Contains("SATA", StringComparison.Ordinal))
        {
            return "SATA";
        }

        if (combined.Contains("SCSI", StringComparison.Ordinal))
        {
            return "SCSI";
        }

        if (combined.Contains("RAID", StringComparison.Ordinal))
        {
            return "RAID";
        }

        if (combined.Contains("SAS", StringComparison.Ordinal))
        {
            return "SAS";
        }

        if (combined.Contains("IDE", StringComparison.Ordinal))
        {
            return "IDE";
        }

        if (combined.Contains("EMMC", StringComparison.Ordinal))
        {
            return "eMMC";
        }

        return string.Empty;
    }

    private static bool IsYes(string value)
        => value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("Oui", StringComparison.OrdinalIgnoreCase);

    private static bool IsOfflineStatus(string value)
        => value.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("hors", StringComparison.OrdinalIgnoreCase);
}

internal sealed record DiskPartDisk(int Number, ulong SizeBytes, bool IsGpt, bool IsOffline);

internal sealed record DiskPartPartition(int Number, string Type);

internal sealed record DiskPartDetailDisk(
    int Number,
    string FriendlyName,
    string SerialNumber,
    string BusType,
    string PartitionStyle,
    ulong SizeBytes,
    bool IsSystem,
    bool IsBoot,
    bool IsReadOnly,
    bool IsOffline);

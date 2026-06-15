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
            Match match = Regex.Match(line, @"^\s*\*?\s*(?:Disk|Disque)\s+(?<number>\d+)\s+(?<status>\S+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            disks.Add(new DiskPartDisk(
                int.Parse(match.Groups["number"].Value),
                ParseFirstSizeBytes(line),
                line.TrimEnd().EndsWith('*'),
                !match.Groups["status"].Value.Equals("Online", StringComparison.OrdinalIgnoreCase)));
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
            Match match = Regex.Match(
                line,
                @"^\s*(?:Partition)\s+(?<number>\d+)\s+(?<type>\S+)",
                RegexOptions.IgnoreCase);

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

            if (friendlyName.Length == 0 && !line.Contains(':', StringComparison.Ordinal))
            {
                friendlyName = line;
                continue;
            }

            if (TryReadKeyValue(line, "Serial Number", out string serial))
            {
                serialNumber = serial;
                continue;
            }

            if (TryReadKeyValue(line, "Type", out string type))
            {
                busType = type;
                continue;
            }

            if (TryReadKeyValue(line, "Status", out string status))
            {
                isOffline = !status.Equals("Online", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (TryReadKeyValue(line, "Current Read-only State", out string currentReadOnly) ||
                TryReadKeyValue(line, "Read-only", out currentReadOnly))
            {
                isReadOnly = IsYes(currentReadOnly);
                continue;
            }

            if (TryReadKeyValue(line, "Boot Disk", out string bootDisk))
            {
                isBoot = IsYes(bootDisk);
                continue;
            }

            if (TryReadKeyValue(line, "System Disk", out string systemDisk))
            {
                isSystem = IsYes(systemDisk);
            }
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

    private static IReadOnlyList<string> SplitLines(string output)
        => output.Split(["\r\n", "\n"], StringSplitOptions.None);

    private static ulong ParseFirstSizeBytes(string line)
    {
        Match match = Regex.Match(line, @"(?<value>\d+)\s*(?<unit>B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return 0;
        }

        ulong value = ulong.Parse(match.Groups["value"].Value);
        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => value,
            "KB" => value * 1024UL,
            "MB" => value * 1024UL * 1024UL,
            "GB" => value * 1024UL * 1024UL * 1024UL,
            "TB" => value * 1024UL * 1024UL * 1024UL * 1024UL,
            _ => 0
        };
    }

    private static bool TryReadKeyValue(string line, string key, out string value)
    {
        value = string.Empty;
        Match match = Regex.Match(line, $"^{Regex.Escape(key)}\\s*:\\s*(?<value>.*)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return true;
    }

    private static bool IsYes(string value)
        => value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("Oui", StringComparison.OrdinalIgnoreCase);
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

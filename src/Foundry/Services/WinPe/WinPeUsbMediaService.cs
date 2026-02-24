using System.Text.Json;

namespace Foundry.Services.WinPe;

internal sealed record WinPeUsbProvisionResult
{
    public string BootDriveLetter { get; init; } = string.Empty;
    public string CacheDriveLetter { get; init; } = string.Empty;
}

internal sealed class WinPeUsbMediaService
{
    private readonly WinPeProcessRunner _processRunner;

    public WinPeUsbMediaService(WinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        WinPeToolPaths tools,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string script = @"
$disks = Get-Disk | Where-Object { $_.BusType -eq 'USB' }
$result = foreach ($disk in $disks) {
    $letters = @(
        Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue |
            Where-Object { $null -ne $_.DriveLetter } |
            ForEach-Object { ""$($_.DriveLetter):"" }
    )

    [pscustomobject]@{
        Number = [int]$disk.Number
        FriendlyName = [string]$disk.FriendlyName
        DriveLetters = ($letters -join "", "")
        SerialNumber = [string]$disk.SerialNumber
        UniqueId = [string]$disk.UniqueId
        BusType = [string]$disk.BusType
        IsRemovable = $disk.IsRemovable
        IsSystem = [bool]$disk.IsSystem
        IsBoot = [bool]$disk.IsBoot
        Size = [uint64]$disk.Size
    }
}

$result | ConvertTo-Json -Compress
";
        WinPeResult<string> result = await RunPowerShellAsync(script, tools, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(result.Error!);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Value!);
            var candidates = new List<WinPeUsbDiskCandidate>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    WinPeUsbDiskCandidate? candidate = ParseUsbCandidate(element);
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                WinPeUsbDiskCandidate? candidate = ParseUsbCandidate(document.RootElement);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            IReadOnlyList<WinPeUsbDiskCandidate> filtered = candidates
                .Where(candidate => candidate.IsRemovable != false && !candidate.IsSystem && !candidate.IsBoot)
                .OrderBy(candidate => candidate.DiskNumber)
                .ToArray();

            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Success(filtered);
        }
        catch (Exception ex)
        {
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "Failed to parse USB disk candidates.",
                ex.Message);
        }
    }

    public async Task<WinPeResult<WinPeUsbProvisionResult>> ProvisionAndPopulateAsync(
        UsbOutputOptions options,
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        if (!options.TargetDiskNumber.HasValue)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "USB target disk number is required.",
                "Set UsbOutputOptions.TargetDiskNumber to the physical disk number you intend to erase.");
        }

        int diskNumber = options.TargetDiskNumber.Value;

        WinPeResult<DiskIdentityInfo> diskResult = await GetDiskIdentityAsync(diskNumber, tools, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!diskResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(diskResult.Error!);
        }

        DiskIdentityInfo disk = diskResult.Value!;

        WinPeResult safetyValidation = ValidateDiskSafety(options, disk);
        if (!safetyValidation.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(safetyValidation.Error!);
        }

        char bootDriveLetter = FindAvailableDriveLetter('\0');
        if (bootDriveLetter == '\0')
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "No free drive letter is available for the BOOT partition.",
                "Free at least two drive letters between D: and Z: and retry.");
        }

        char cacheDriveLetter = FindAvailableDriveLetter(bootDriveLetter);
        if (cacheDriveLetter == '\0')
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "No free drive letter is available for the cache partition.",
                "Free at least one drive letter between D: and Z: and retry.");
        }

        WinPeResult provisioningResult = await ProvisionDiskAsync(
            diskNumber,
            options.PartitionStyle,
            options.FormatMode,
            bootDriveLetter,
            cacheDriveLetter,
            tools,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!provisioningResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(provisioningResult.Error!);
        }

        string bootRoot = $"{bootDriveLetter}:\\";
        string cacheRoot = $"{cacheDriveLetter}:\\";

        WinPeResult copyResult = await CopyMediaAsync(artifact.MediaDirectoryPath, bootRoot, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!copyResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(copyResult.Error!);
        }

        WinPeResult verifyResult = VerifyBootArtifacts(bootRoot, artifact.Architecture);
        if (!verifyResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(verifyResult.Error!);
        }

        string cacheMarkerPath = Path.Combine(cacheRoot, "Foundry Cache");
        Directory.CreateDirectory(cacheMarkerPath);
        Directory.CreateDirectory(Path.Combine(cacheRoot, "Runtime"));
        Directory.CreateDirectory(Path.Combine(cacheRoot, "OperatingSystem"));
        Directory.CreateDirectory(Path.Combine(cacheRoot, "DriverPack"));

        return WinPeResult<WinPeUsbProvisionResult>.Success(new WinPeUsbProvisionResult
        {
            BootDriveLetter = $"{bootDriveLetter}:",
            CacheDriveLetter = $"{cacheDriveLetter}:"
        });
    }

    private static WinPeResult ValidateDiskSafety(UsbOutputOptions options, DiskIdentityInfo disk)
    {
        if (!disk.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbUnsafeTarget,
                "Target disk is not on USB bus.",
                $"Disk {disk.Number} bus type is '{disk.BusType}'. Only USB disks are allowed.");
        }

        if (disk.IsRemovable == false)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbUnsafeTarget,
                "Target disk is not removable.",
                $"Disk {disk.Number} reports IsRemovable=false.");
        }

        if (disk.IsSystem || disk.IsBoot)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbUnsafeTarget,
                "Refusing to modify a system or boot disk.",
                $"Disk {disk.Number}: IsSystem={disk.IsSystem}, IsBoot={disk.IsBoot}.");
        }

        if (string.IsNullOrWhiteSpace(options.ExpectedDiskFriendlyName) &&
            string.IsNullOrWhiteSpace(options.ExpectedDiskSerialNumber) &&
            string.IsNullOrWhiteSpace(options.ExpectedDiskUniqueId))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Disk identity confirmation is required.",
                "Set at least one of ExpectedDiskFriendlyName, ExpectedDiskSerialNumber, or ExpectedDiskUniqueId.");
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedDiskFriendlyName) &&
            !ContainsIgnoreCase(disk.FriendlyName, options.ExpectedDiskFriendlyName))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbIdentityMismatch,
                "Target disk friendly name does not match confirmation.",
                $"Expected contains '{options.ExpectedDiskFriendlyName}', actual '{disk.FriendlyName}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedDiskSerialNumber) &&
            !ContainsIgnoreCase(disk.SerialNumber, options.ExpectedDiskSerialNumber))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbIdentityMismatch,
                "Target disk serial number does not match confirmation.",
                $"Expected contains '{options.ExpectedDiskSerialNumber}', actual '{disk.SerialNumber}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedDiskUniqueId) &&
            !ContainsIgnoreCase(disk.UniqueId, options.ExpectedDiskUniqueId))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbIdentityMismatch,
                "Target disk unique ID does not match confirmation.",
                $"Expected contains '{options.ExpectedDiskUniqueId}', actual '{disk.UniqueId}'.");
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ProvisionDiskAsync(
        int diskNumber,
        UsbPartitionStyle partitionStyle,
        UsbFormatMode formatMode,
        char bootDriveLetter,
        char cacheDriveLetter,
        WinPeToolPaths tools,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string[] conversionLines = partitionStyle == UsbPartitionStyle.Gpt
            ? ["convert mbr noerr", "convert gpt"]
            : ["convert mbr"];
        string activeLine = partitionStyle == UsbPartitionStyle.Mbr ? "active" : "";

        string formatSuffix = formatMode == UsbFormatMode.Quick ? " quick" : string.Empty;
        string[] scriptLines = [
            $"select disk {diskNumber}",
            "online disk noerr",
            "attributes disk clear readonly noerr",
            "clean",
            ..conversionLines,
            "create partition primary size=4096",
            $"format fs=fat32{formatSuffix} label=BOOT",
            $"assign letter={bootDriveLetter}",
            activeLine,
            "create partition primary",
            $"format fs=ntfs{formatSuffix} label=\"Foundry Cache\"",
            $"assign letter={cacheDriveLetter}"
        ];

        string[] effectiveScriptLines = scriptLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        string scriptPath = Path.Combine(workingDirectory, "diskpart-usb.txt");
        File.WriteAllLines(scriptPath, effectiveScriptLines);

        WinPeProcessExecution diskPartResult = await _processRunner.RunAsync(
            "diskpart.exe",
            $"/s {WinPeProcessRunner.Quote(scriptPath)}",
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!diskPartResult.IsSuccess)
        {
            string diagnostic = $"{diskPartResult.ToDiagnosticText()}{Environment.NewLine}" +
                                $"PartitionStyle: {partitionStyle}{Environment.NewLine}" +
                                "DiskPartScript:" + Environment.NewLine +
                                string.Join(Environment.NewLine, effectiveScriptLines);
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "Failed to partition and format the USB disk.",
                diagnostic);
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> CopyMediaAsync(
        string sourceMediaDirectory,
        string destinationBootRoot,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string robocopyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "robocopy.exe");
        if (!File.Exists(robocopyPath))
        {
            robocopyPath = "robocopy.exe";
        }

        WinPeProcessExecution copyResult = await _processRunner.RunAsync(
            robocopyPath,
            $"{WinPeProcessRunner.Quote(sourceMediaDirectory)} {WinPeProcessRunner.Quote(destinationBootRoot)} /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP",
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        // Robocopy success range: 0-7.
        if (copyResult.ExitCode <= 7)
        {
            return WinPeResult.Success();
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.UsbCopyFailed,
            "Failed to copy WinPE media files to USB BOOT partition.",
            copyResult.ToDiagnosticText());
    }

    private static WinPeResult VerifyBootArtifacts(string bootRoot, WinPeArchitecture architecture)
    {
        string bootWimPath = Path.Combine(bootRoot, "sources", "boot.wim");
        if (!File.Exists(bootWimPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: boot.wim not found.",
                $"Expected '{bootWimPath}'.");
        }

        string bcdPath = Path.Combine(bootRoot, "boot", "BCD");
        if (!File.Exists(bcdPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: BCD not found.",
                $"Expected '{bcdPath}'.");
        }

        string efiBootPath = Path.Combine(bootRoot, "EFI", "Boot", architecture.ToBootEfiName());
        if (!File.Exists(efiBootPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: EFI boot file not found.",
                $"Expected '{efiBootPath}'.");
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult<DiskIdentityInfo>> GetDiskIdentityAsync(
        int diskNumber,
        WinPeToolPaths tools,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string script = $@"
$disk = Get-Disk -Number {diskNumber} -ErrorAction Stop
[pscustomobject]@{{
    Number = [int]$disk.Number
    FriendlyName = [string]$disk.FriendlyName
    SerialNumber = [string]$disk.SerialNumber
    UniqueId = [string]$disk.UniqueId
    BusType = [string]$disk.BusType
    IsRemovable = $disk.IsRemovable
    IsSystem = [bool]$disk.IsSystem
    IsBoot = [bool]$disk.IsBoot
}} | ConvertTo-Json -Compress
";

        WinPeResult<string> scriptResult = await RunPowerShellAsync(script, tools, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!scriptResult.IsSuccess)
        {
            return WinPeResult<DiskIdentityInfo>.Failure(scriptResult.Error!);
        }

        try
        {
            var disk = JsonSerializer.Deserialize<DiskIdentityInfo>(scriptResult.Value!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (disk is null)
            {
                return WinPeResult<DiskIdentityInfo>.Failure(
                    WinPeErrorCodes.UsbQueryFailed,
                    "Failed to read target USB disk details.",
                    "PowerShell returned an empty payload for Get-Disk.");
            }

            return WinPeResult<DiskIdentityInfo>.Success(disk);
        }
        catch (Exception ex)
        {
            return WinPeResult<DiskIdentityInfo>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "Failed to parse target USB disk details.",
                ex.Message);
        }
    }

    private async Task<WinPeResult<string>> RunPowerShellAsync(
        string script,
        WinPeToolPaths tools,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string encodedScript = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

        WinPeProcessExecution result = await _processRunner.RunAsync(
            tools.PowerShellPath,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "A required PowerShell USB query command failed.",
                result.ToDiagnosticText());
        }

        string output = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "A required PowerShell USB query command returned no data.",
                result.ToDiagnosticText());
        }

        return WinPeResult<string>.Success(output);
    }

    private static WinPeUsbDiskCandidate? ParseUsbCandidate(JsonElement element)
    {
        if (!TryGetInt32(element, "Number", out int diskNumber))
        {
            return null;
        }

        return new WinPeUsbDiskCandidate
        {
            DiskNumber = diskNumber,
            FriendlyName = GetString(element, "FriendlyName"),
            DriveLetters = GetString(element, "DriveLetters"),
            SerialNumber = GetString(element, "SerialNumber"),
            UniqueId = GetString(element, "UniqueId"),
            BusType = GetString(element, "BusType"),
            IsRemovable = GetNullableBool(element, "IsRemovable"),
            IsSystem = GetBool(element, "IsSystem"),
            IsBoot = GetBool(element, "IsBoot"),
            SizeBytes = GetUInt64(element, "Size")
        };
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out value);
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out int parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
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

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out bool parsed))
        {
            return parsed;
        }

        return false;
    }

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out bool parsed))
        {
            return parsed;
        }

        return null;
    }

    private static ulong GetUInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetUInt64(out ulong value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            ulong.TryParse(property.GetString(), out ulong parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static char FindAvailableDriveLetter(char excludedLetter)
    {
        var usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        for (char letter = 'D'; letter <= 'Z'; letter++)
        {
            if (letter == char.ToUpperInvariant(excludedLetter))
            {
                continue;
            }

            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        return '\0';
    }

    private static bool ContainsIgnoreCase(string source, string expectedFragment)
    {
        return source.IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class DiskIdentityInfo
    {
        public int Number { get; init; }
        public string FriendlyName { get; init; } = string.Empty;
        public string SerialNumber { get; init; } = string.Empty;
        public string UniqueId { get; init; } = string.Empty;
        public string BusType { get; init; } = string.Empty;
        public bool? IsRemovable { get; init; }
        public bool IsSystem { get; init; }
        public bool IsBoot { get; init; }
        public ulong Size { get; init; }
    }
}

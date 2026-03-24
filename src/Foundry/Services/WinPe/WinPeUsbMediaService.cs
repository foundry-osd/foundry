using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed record WinPeUsbProvisionResult
{
    public string BootDriveLetter { get; init; } = string.Empty;
    public string CacheDriveLetter { get; init; } = string.Empty;
}

internal sealed class WinPeUsbMediaService
{
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeUsbMediaService> _logger;

    public WinPeUsbMediaService(WinPeProcessRunner processRunner, ILogger<WinPeUsbMediaService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        WinPeToolPaths tools,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Querying USB disk candidates. WorkingDirectory={WorkingDirectory}", workingDirectory);
        string script = @"
$disks = Get-Disk | Where-Object { $_.BusType -eq 'USB' }
$result = @(
foreach ($disk in $disks) {
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
)

if ($result.Count -eq 0) {
    '[]'
}
else {
    $result | ConvertTo-Json -Compress
}
";
        WinPeResult<string> result = await RunPowerShellAsync(script, tools, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("USB disk candidate query failed. Code={ErrorCode}, Message={ErrorMessage}", result.Error?.Code, result.Error?.Message);
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

            _logger.LogInformation("Resolved {CandidateCount} USB disk candidates after filtering.", filtered.Count);
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Success(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse USB disk candidates from PowerShell output.");
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
        bool useBootEx,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting USB provisioning for TargetDiskNumber={TargetDiskNumber}, PartitionStyle={PartitionStyle}, FormatMode={FormatMode}",
            options.TargetDiskNumber,
            options.PartitionStyle,
            options.FormatMode);

        if (!options.TargetDiskNumber.HasValue)
        {
            _logger.LogWarning("USB provisioning validation failed: target disk number is missing.");
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "USB target disk number is required.",
                "Set UsbOutputOptions.TargetDiskNumber to the physical disk number you intend to erase.");
        }

        int diskNumber = options.TargetDiskNumber.Value;

        WinPeResult<DiskIdentityInfo> diskResult = await GetDiskIdentityAsync(diskNumber, tools, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!diskResult.IsSuccess)
        {
            _logger.LogWarning("Failed to query target USB disk identity. DiskNumber={DiskNumber}, Code={ErrorCode}, Message={ErrorMessage}",
                diskNumber,
                diskResult.Error?.Code,
                diskResult.Error?.Message);
            return WinPeResult<WinPeUsbProvisionResult>.Failure(diskResult.Error!);
        }

        DiskIdentityInfo disk = diskResult.Value!;

        WinPeResult safetyValidation = ValidateDiskSafety(options, disk);
        if (!safetyValidation.IsSuccess)
        {
            _logger.LogWarning("USB safety validation failed. DiskNumber={DiskNumber}, Code={ErrorCode}, Message={ErrorMessage}",
                diskNumber,
                safetyValidation.Error?.Code,
                safetyValidation.Error?.Message);
            return WinPeResult<WinPeUsbProvisionResult>.Failure(safetyValidation.Error!);
        }

        char bootDriveLetter = FindAvailableDriveLetter('\0');
        if (bootDriveLetter == '\0')
        {
            _logger.LogWarning("USB provisioning failed: no free drive letter available for BOOT partition.");
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "No free drive letter is available for the BOOT partition.",
                "Free at least two drive letters between D: and Z: and retry.");
        }

        char cacheDriveLetter = FindAvailableDriveLetter(bootDriveLetter);
        if (cacheDriveLetter == '\0')
        {
            _logger.LogWarning("USB provisioning failed: no free drive letter available for cache partition.");
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "No free drive letter is available for the cache partition.",
                "Free at least one drive letter between D: and Z: and retry.");
        }

        _logger.LogInformation(
            "Resolved USB target disk identity and drive letters. DiskNumber={DiskNumber}, FriendlyName={FriendlyName}, BootDriveLetter={BootDriveLetter}, CacheDriveLetter={CacheDriveLetter}",
            diskNumber,
            disk.FriendlyName,
            $"{bootDriveLetter}:",
            $"{cacheDriveLetter}:");
        WinPeResult provisioningResult = await ProvisionDiskAsync(
            diskNumber,
            options.PartitionStyle,
            options.FormatMode,
            bootDriveLetter,
            cacheDriveLetter,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!provisioningResult.IsSuccess)
        {
            _logger.LogWarning("USB disk provisioning command failed. Code={ErrorCode}, Message={ErrorMessage}",
                provisioningResult.Error?.Code,
                provisioningResult.Error?.Message);
            return WinPeResult<WinPeUsbProvisionResult>.Failure(provisioningResult.Error!);
        }

        string bootRoot = $"{bootDriveLetter}:\\";
        string cacheRoot = $"{cacheDriveLetter}:\\";
        _logger.LogInformation("USB disk partitioning and formatting completed. DiskNumber={DiskNumber}, BootRoot={BootRoot}, CacheRoot={CacheRoot}", diskNumber, bootRoot, cacheRoot);

        _logger.LogInformation("Copying prepared WinPE media to USB boot partition. SourceMediaDirectoryPath={SourceMediaDirectoryPath}, BootRoot={BootRoot}", artifact.MediaDirectoryPath, bootRoot);
        WinPeResult copyResult = await CopyMediaAsync(artifact.MediaDirectoryPath, bootRoot, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!copyResult.IsSuccess)
        {
            _logger.LogWarning("USB media copy failed. Code={ErrorCode}, Message={ErrorMessage}", copyResult.Error?.Code, copyResult.Error?.Message);
            return WinPeResult<WinPeUsbProvisionResult>.Failure(copyResult.Error!);
        }

        _logger.LogInformation("Copied prepared WinPE media to USB boot partition successfully. BootRoot={BootRoot}", bootRoot);
        if (useBootEx)
        {
            _logger.LogInformation("Reconfiguring USB boot files with BootEx support. BootRoot={BootRoot}, PartitionStyle={PartitionStyle}", bootRoot, options.PartitionStyle);
            WinPeResult bootConfigResult = ConfigureBootFiles(bootRoot, artifact);
            if (!bootConfigResult.IsSuccess)
            {
                _logger.LogWarning("USB BootEx boot file configuration failed. Code={ErrorCode}, Message={ErrorMessage}", bootConfigResult.Error?.Code, bootConfigResult.Error?.Message);
                return WinPeResult<WinPeUsbProvisionResult>.Failure(bootConfigResult.Error!);
            }

            _logger.LogInformation("USB boot files reconfigured with BootEx support successfully. BootRoot={BootRoot}", bootRoot);
        }

        WinPeResult verifyResult = VerifyBootArtifacts(bootRoot, artifact.Architecture);
        if (!verifyResult.IsSuccess)
        {
            _logger.LogWarning("USB boot artifact verification failed. Code={ErrorCode}, Message={ErrorMessage}", verifyResult.Error?.Code, verifyResult.Error?.Message);
            return WinPeResult<WinPeUsbProvisionResult>.Failure(verifyResult.Error!);
        }

        _logger.LogInformation("Verified USB boot artifacts successfully. BootRoot={BootRoot}, Architecture={Architecture}", bootRoot, artifact.Architecture);
        Directory.CreateDirectory(Path.Combine(cacheRoot, "Runtime"));
        Directory.CreateDirectory(Path.Combine(cacheRoot, "OperatingSystem"));
        Directory.CreateDirectory(Path.Combine(cacheRoot, "DriverPack"));
        _logger.LogInformation("Initialized USB cache partition directories. CacheRoot={CacheRoot}", cacheRoot);

        WinPeResult<WinPeUsbProvisionResult> success = WinPeResult<WinPeUsbProvisionResult>.Success(new WinPeUsbProvisionResult
        {
            BootDriveLetter = $"{bootDriveLetter}:",
            CacheDriveLetter = $"{cacheDriveLetter}:"
        });

        _logger.LogInformation("USB provisioning completed successfully. DiskNumber={DiskNumber}, BootDrive={BootDrive}, CacheDrive={CacheDrive}",
            diskNumber,
            success.Value?.BootDriveLetter,
            success.Value?.CacheDriveLetter);
        return success;
    }

    private WinPeResult ConfigureBootFiles(
        string bootRoot,
        WinPeBuildArtifact artifact)
    {
        string bootManagerSourcePath = Path.Combine(artifact.WorkingDirectoryPath, "bootbins", "bootmgfw_EX.efi");
        if (!File.Exists(bootManagerSourcePath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BootExUnsupported,
                "PCA2023 USB creation requires BootEx EFI binaries in the WinPE workspace.",
                $"Expected '{bootManagerSourcePath}'.");
        }

        string efiBootPath = Path.Combine(bootRoot, "EFI", "Boot", artifact.Architecture.ToBootEfiName());
        if (File.Exists(efiBootPath))
        {
            File.Copy(bootManagerSourcePath, efiBootPath, overwrite: true);
        }

        string efiMicrosoftBootManagerPath = Path.Combine(bootRoot, "EFI", "Microsoft", "Boot", "bootmgfw.efi");
        string? efiMicrosoftBootDirectoryPath = Path.GetDirectoryName(efiMicrosoftBootManagerPath);
        if (string.IsNullOrWhiteSpace(efiMicrosoftBootDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbProvisioningFailed,
                "USB boot configuration failed: EFI Microsoft boot manager path is invalid.",
                $"Expected '{efiMicrosoftBootManagerPath}'.");
        }

        Directory.CreateDirectory(efiMicrosoftBootDirectoryPath);
        File.Copy(bootManagerSourcePath, efiMicrosoftBootManagerPath, overwrite: true);
        return WinPeResult.Success();
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
            "select partition 1",
            $"assign letter={bootDriveLetter}",
            $"select volume={bootDriveLetter}",
            $"format fs=fat32{formatSuffix} label=BOOT",
            activeLine,
            "create partition primary",
            "select partition 2",
            $"assign letter={cacheDriveLetter}",
            $"select volume={cacheDriveLetter}",
            $"format fs=ntfs{formatSuffix} label=\"Foundry Cache\""
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
            _logger.LogWarning("DiskPart USB provisioning failed for DiskNumber={DiskNumber}. Diagnostic={Diagnostic}", diskNumber, diagnostic);
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
            _logger.LogDebug("Robocopy completed successfully for USB media copy. ExitCode={ExitCode}", copyResult.ExitCode);
            return WinPeResult.Success();
        }

        _logger.LogWarning("Robocopy failed for USB media copy. ExitCode={ExitCode}, Diagnostic={Diagnostic}", copyResult.ExitCode, copyResult.ToDiagnosticText());
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
            _logger.LogError(ex, "Failed to parse target USB disk identity. DiskNumber={DiskNumber}", diskNumber);
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
            _logger.LogWarning("PowerShell USB helper command failed. Code={ExitCode}, Diagnostic={Diagnostic}", result.ExitCode, result.ToDiagnosticText());
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "A required PowerShell USB query command failed.",
                result.ToDiagnosticText());
        }

        string output = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning("PowerShell USB helper command returned empty output.");
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

using System.Text;
using System.Text.Json;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeUsbMediaService : IWinPeUsbMediaService
{
    private readonly IWinPeProcessRunner _processRunner;
    private readonly IWinPeRuntimePayloadProvisioningService _runtimePayloadProvisioningService;

    public WinPeUsbMediaService()
        : this(new WinPeProcessRunner(), new WinPeRuntimePayloadProvisioningService())
    {
    }

    internal WinPeUsbMediaService(IWinPeProcessRunner processRunner)
        : this(processRunner, new WinPeRuntimePayloadProvisioningService(processRunner))
    {
    }

    internal WinPeUsbMediaService(
        IWinPeProcessRunner processRunner,
        IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService)
    {
        _processRunner = processRunner;
        _runtimePayloadProvisioningService = runtimePayloadProvisioningService;
    }

    public async Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(
        WinPeToolPaths tools,
        string workingDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(tools.PowerShellPath))
        {
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "PowerShell path is required to query USB disks.",
                "Set WinPeToolPaths.PowerShellPath.");
        }

        if (string.IsNullOrWhiteSpace(workingDirectoryPath))
        {
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "USB query working directory is required.",
                "Provide a working directory for the USB disk query.");
        }

        Directory.CreateDirectory(workingDirectoryPath);

        const string script = """
                              $disks = Get-Disk | Where-Object { $_.BusType -eq 'USB' }
                              $result = @(
                              foreach ($disk in $disks) {
                                  $letters = @(
                                      Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue |
                                          Where-Object { $null -ne $_.DriveLetter } |
                                          ForEach-Object { "$($_.DriveLetter):" }
                                  )

                                  [pscustomobject]@{
                                      Number = [int]$disk.Number
                                      FriendlyName = [string]$disk.FriendlyName
                                      DriveLetters = ($letters -join ", ")
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
                              """;

        WinPeResult<string> result = await RunPowerShellAsync(
            script,
            tools,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(result.Error!);
        }

        try
        {
            IReadOnlyList<WinPeUsbDiskCandidate> candidates = ParseUsbCandidates(result.Value!)
                .Where(candidate =>
                    candidate.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
                    candidate.IsRemovable != false &&
                    !candidate.IsSystem &&
                    !candidate.IsBoot)
                .OrderBy(candidate => candidate.DiskNumber)
                .ToArray();

            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Success(candidates);
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
        bool useBootEx,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifact);

        cancellationToken.ThrowIfCancellationRequested();

        if (!options.TargetDiskNumber.HasValue)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "USB target disk number is required.",
                "Set UsbOutputOptions.TargetDiskNumber to the physical disk number you intend to erase.");
        }

        int diskNumber = options.TargetDiskNumber.Value;
        ReportProgress(options.Progress, 0, "Validating USB target.");
        WinPeResult<WinPeUsbDiskIdentity> diskResult = await GetDiskIdentityAsync(
            diskNumber,
            tools,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!diskResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(diskResult.Error!);
        }

        ReportProgress(options.Progress, 10, "Checking USB target safety.");
        WinPeResult safetyValidation = ValidateDiskSafety(options, diskResult.Value!);
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

        ReportProgress(options.Progress, 20, "Partitioning and formatting USB target.");
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
            return WinPeResult<WinPeUsbProvisionResult>.Failure(provisioningResult.Error!);
        }

        string bootRootPath = $"{bootDriveLetter}:\\";
        string cacheRootPath = $"{cacheDriveLetter}:\\";
        ReportProgress(options.Progress, 55, "Copying WinPE media to USB.");
        WinPeResult copyResult = await CopyMediaAsync(
            artifact.MediaDirectoryPath,
            bootRootPath,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!copyResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(copyResult.Error!);
        }

        if (useBootEx)
        {
            ReportProgress(options.Progress, 70, "Configuring USB boot files.");
            WinPeResult bootConfigurationResult = ConfigureBootFiles(bootRootPath, artifact);
            if (!bootConfigurationResult.IsSuccess)
            {
                return WinPeResult<WinPeUsbProvisionResult>.Failure(bootConfigurationResult.Error!);
            }
        }

        ReportProgress(options.Progress, 78, "Verifying USB boot media.");
        WinPeResult verificationResult = VerifyBootArtifacts(bootRootPath, artifact.Architecture);
        if (!verificationResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(verificationResult.Error!);
        }

        WinPeResult bootLayoutResult = VerifyBootPartitionLayout(bootRootPath);
        if (!bootLayoutResult.IsSuccess)
        {
            return WinPeResult<WinPeUsbProvisionResult>.Failure(bootLayoutResult.Error!);
        }

        ReportProgress(options.Progress, 85, "Preparing USB cache partition.");
        InitializeCachePartitionDirectories(cacheRootPath);

        if (options.RuntimePayloadProvisioning is not null)
        {
            ReportProgress(options.Progress, 92, "Provisioning USB runtime payloads.");
            WinPeResult runtimePayloadResult = await _runtimePayloadProvisioningService.ProvisionAsync(
                CreateUsbRuntimePayloadOptions(options.RuntimePayloadProvisioning, artifact, cacheRootPath),
                cancellationToken).ConfigureAwait(false);

            if (!runtimePayloadResult.IsSuccess)
            {
                return WinPeResult<WinPeUsbProvisionResult>.Failure(runtimePayloadResult.Error!);
            }
        }

        ReportProgress(options.Progress, 100, "USB media completed.");
        return WinPeResult<WinPeUsbProvisionResult>.Success(new WinPeUsbProvisionResult
        {
            BootDriveLetter = $"{bootDriveLetter}:",
            CacheDriveLetter = $"{cacheDriveLetter}:"
        });
    }

    internal static WinPeResult ValidateDiskSafety(UsbOutputOptions options, WinPeUsbDiskIdentity disk)
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
                "Set at least one expected disk identity value before formatting USB media.");
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

    internal static bool IsRobocopySuccessExitCode(int exitCode)
    {
        return exitCode is >= 0 and <= 7;
    }

    internal static IReadOnlyList<string> BuildDiskPartScript(
        int diskNumber,
        UsbPartitionStyle partitionStyle,
        UsbFormatMode formatMode,
        char bootDriveLetter,
        char cacheDriveLetter)
    {
        string[] conversionLines = partitionStyle == UsbPartitionStyle.Gpt
            ? ["convert mbr noerr", "convert gpt"]
            : ["convert mbr"];
        string activeLine = partitionStyle == UsbPartitionStyle.Mbr ? "active" : string.Empty;
        string formatSuffix = formatMode == UsbFormatMode.Quick ? " quick" : string.Empty;

        string[] scriptLines =
        [
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

        return scriptLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    internal static void InitializeCachePartitionDirectories(string cacheRootPath)
    {
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "Runtime"));
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "Cache", "OperatingSystems"));
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "Cache", "DriverPacks"));
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "Cache", "Firmware"));
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "State"));
        Directory.CreateDirectory(Path.Combine(cacheRootPath, "Temp"));
    }

    internal static WinPeRuntimePayloadProvisioningOptions CreateUsbRuntimePayloadOptions(
        WinPeRuntimePayloadProvisioningOptions options,
        WinPeBuildArtifact artifact,
        string cacheRootPath)
    {
        return options with
        {
            MountedImagePath = string.Empty,
            UsbCacheRootPath = cacheRootPath,
            WorkingDirectoryPath = string.IsNullOrWhiteSpace(options.WorkingDirectoryPath)
                ? artifact.WorkingDirectoryPath
                : options.WorkingDirectoryPath,
            Architecture = artifact.Architecture
        };
    }

    internal static WinPeResult ConfigureBootFiles(
        string bootRootPath,
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

        string efiBootPath = Path.Combine(bootRootPath, "EFI", "Boot", artifact.Architecture.ToBootEfiName());
        if (File.Exists(efiBootPath))
        {
            File.Copy(bootManagerSourcePath, efiBootPath, overwrite: true);
        }

        string efiMicrosoftBootManagerPath = Path.Combine(bootRootPath, "EFI", "Microsoft", "Boot", "bootmgfw.efi");
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

    internal static WinPeResult VerifyBootArtifacts(string bootRootPath, WinPeArchitecture architecture)
    {
        string bootWimPath = Path.Combine(bootRootPath, "sources", "boot.wim");
        if (!File.Exists(bootWimPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: boot.wim not found.",
                $"Expected '{bootWimPath}'.");
        }

        string bcdPath = Path.Combine(bootRootPath, "boot", "BCD");
        if (!File.Exists(bcdPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: BCD not found.",
                $"Expected '{bcdPath}'.");
        }

        string efiBootPath = Path.Combine(bootRootPath, "EFI", "Boot", architecture.ToBootEfiName());
        if (!File.Exists(efiBootPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: EFI boot file not found.",
                $"Expected '{efiBootPath}'.");
        }

        return WinPeResult.Success();
    }

    internal static WinPeResult VerifyBootPartitionLayout(string bootRootPath)
    {
        string foundryPath = Path.Combine(bootRootPath, "Foundry");
        if (Directory.Exists(foundryPath) || File.Exists(foundryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.UsbVerificationFailed,
                "USB verification failed: BOOT partition contains Foundry runtime content.",
                $"Unexpected path: '{foundryPath}'.");
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ProvisionDiskAsync(
        int diskNumber,
        UsbPartitionStyle partitionStyle,
        UsbFormatMode formatMode,
        char bootDriveLetter,
        char cacheDriveLetter,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> diskPartScript = BuildDiskPartScript(
            diskNumber,
            partitionStyle,
            formatMode,
            bootDriveLetter,
            cacheDriveLetter);

        string scriptPath = Path.Combine(workingDirectoryPath, "diskpart-usb.txt");
        Directory.CreateDirectory(workingDirectoryPath);
        await File.WriteAllLinesAsync(scriptPath, diskPartScript, cancellationToken).ConfigureAwait(false);

        WinPeProcessExecution execution = await _processRunner.RunAsync(
            "diskpart.exe",
            $"/s {WinPeProcessRunner.Quote(scriptPath)}",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (execution.IsSuccess)
        {
            return WinPeResult.Success();
        }

        string diagnostic = $"{execution.ToDiagnosticText()}{Environment.NewLine}" +
                            $"PartitionStyle: {partitionStyle}{Environment.NewLine}" +
                            "DiskPartScript:" + Environment.NewLine +
                            string.Join(Environment.NewLine, diskPartScript);
        return WinPeResult.Failure(
            WinPeErrorCodes.UsbProvisioningFailed,
            "Failed to partition and format the USB disk.",
            diagnostic);
    }

    private async Task<WinPeResult> CopyMediaAsync(
        string sourceMediaDirectoryPath,
        string destinationBootRootPath,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string robocopyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "robocopy.exe");
        if (!File.Exists(robocopyPath))
        {
            robocopyPath = "robocopy.exe";
        }

        WinPeProcessExecution execution = await _processRunner.RunAsync(
            robocopyPath,
            $"{WinPeProcessRunner.Quote(sourceMediaDirectoryPath)} {WinPeProcessRunner.Quote(destinationBootRootPath)} /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (IsRobocopySuccessExitCode(execution.ExitCode))
        {
            return WinPeResult.Success();
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.UsbCopyFailed,
            "Failed to copy WinPE media files to USB BOOT partition.",
            execution.ToDiagnosticText());
    }

    private static void ReportProgress(IProgress<WinPeMediaProgress>? progress, int percent, string status)
    {
        progress?.Report(new WinPeMediaProgress
        {
            Percent = Math.Clamp(percent, 0, 100),
            Status = status
        });
    }

    private async Task<WinPeResult<WinPeUsbDiskIdentity>> GetDiskIdentityAsync(
        int diskNumber,
        WinPeToolPaths tools,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string script = $$"""
                          $disk = Get-Disk -Number {{diskNumber}} -ErrorAction Stop
                          [pscustomobject]@{
                              Number = [int]$disk.Number
                              FriendlyName = [string]$disk.FriendlyName
                              SerialNumber = [string]$disk.SerialNumber
                              UniqueId = [string]$disk.UniqueId
                              BusType = [string]$disk.BusType
                              IsRemovable = $disk.IsRemovable
                              IsSystem = [bool]$disk.IsSystem
                              IsBoot = [bool]$disk.IsBoot
                              Size = [uint64]$disk.Size
                          } | ConvertTo-Json -Compress
                          """;

        WinPeResult<string> result = await RunPowerShellAsync(
            script,
            tools,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return WinPeResult<WinPeUsbDiskIdentity>.Failure(result.Error!);
        }

        try
        {
            WinPeUsbDiskIdentity? disk = JsonSerializer.Deserialize<WinPeUsbDiskIdentity>(
                result.Value!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return disk is null
                ? WinPeResult<WinPeUsbDiskIdentity>.Failure(
                    WinPeErrorCodes.UsbQueryFailed,
                    "Failed to read target USB disk details.",
                    "PowerShell returned an empty payload for Get-Disk.")
                : WinPeResult<WinPeUsbDiskIdentity>.Success(disk);
        }
        catch (Exception ex)
        {
            return WinPeResult<WinPeUsbDiskIdentity>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "Failed to parse target USB disk details.",
                ex.Message);
        }
    }

    private static char FindAvailableDriveLetter(char excludedLetter)
    {
        HashSet<char> usedLetters = DriveInfo.GetDrives()
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

    private async Task<WinPeResult<string>> RunPowerShellAsync(
        string script,
        WinPeToolPaths tools,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        WinPeProcessExecution execution = await _processRunner.RunAsync(
            tools.PowerShellPath,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "A required PowerShell USB query command failed.",
                execution.ToDiagnosticText());
        }

        string output = execution.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.UsbQueryFailed,
                "A required PowerShell USB query command returned no data.",
                execution.ToDiagnosticText());
        }

        return WinPeResult<string>.Success(output);
    }

    private static IReadOnlyList<WinPeUsbDiskCandidate> ParseUsbCandidates(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
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

        return candidates;
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

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            ? property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString()
            : string.Empty;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        return property.ValueKind == JsonValueKind.String &&
               bool.TryParse(property.GetString(), out bool parsed) &&
               parsed;
    }

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        return property.ValueKind == JsonValueKind.String &&
               bool.TryParse(property.GetString(), out bool parsed)
            ? parsed
            : null;
    }

    private static ulong GetUInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt64(out ulong value))
        {
            return value;
        }

        return property.ValueKind == JsonValueKind.String &&
               ulong.TryParse(property.GetString(), out ulong parsed)
            ? parsed
            : 0;
    }

    private static bool ContainsIgnoreCase(string source, string expectedFragment)
    {
        return source.IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

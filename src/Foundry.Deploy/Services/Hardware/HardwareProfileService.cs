using System.Text.Json;
using System.Text;
using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Hardware;

public sealed class HardwareProfileService : IHardwareProfileService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<HardwareProfileService> _logger;

    public HardwareProfileService(IProcessRunner processRunner, ILogger<HardwareProfileService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Detecting current hardware profile.");
        string script = @"
function ConvertTo-TrimmedString {
    param (
        [Parameter(ValueFromPipeline = $true)]
        $Value
    )

    process {
        if ($null -eq $Value) {
            return ''
        }

        return $Value.ToString().Trim()
    }
}

$computer = Get-CimInstance -ClassName Win32_ComputerSystem
$product = Get-CimInstance -ClassName Win32_ComputerSystemProduct
$bios = Get-CimInstance -ClassName Win32_BIOS
$tpm = Get-CimInstance -Namespace 'ROOT\cimv2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction SilentlyContinue
$battery = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue
$pnpDevices = @(Get-CimInstance -ClassName Win32_PnpEntity -Property Name,DeviceID,HardwareID,ClassGuid,Manufacturer,PNPClass -ErrorAction SilentlyContinue | ForEach-Object {
    $hardwareIds = @($_.HardwareID | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.ToString().Trim() })
    [pscustomobject]@{
        Name = [string]($_.Name | ConvertTo-TrimmedString)
        DeviceId = [string]($_.DeviceID | ConvertTo-TrimmedString)
        HardwareIds = $hardwareIds
        ClassGuid = [string]($_.ClassGuid | ConvertTo-TrimmedString)
        Manufacturer = [string]($_.Manufacturer | ConvertTo-TrimmedString)
        PnpClass = [string]($_.PNPClass | ConvertTo-TrimmedString)
    }
})
$firmwareDevice = $pnpDevices | Where-Object { $_.ClassGuid -eq '{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}' } | Select-Object -First 1
$systemFirmwareHardwareId = ''
if ($firmwareDevice -and $firmwareDevice.DeviceId -match '\{?(([0-9a-f]){8}-([0-9a-f]){4}-([0-9a-f]){4}-([0-9a-f]){4}-([0-9a-f]){12})\}?') {
    $systemFirmwareHardwareId = $Matches[1]
}
$isOnBattery = @($battery | Where-Object { $_.BatteryStatus -eq 1 }).Count -gt 0

[pscustomobject]@{
    Manufacturer = [string]$computer.Manufacturer
    Model = [string]$computer.Model
    Product = [string]$product.Version
    SerialNumber = [string]$bios.SerialNumber
    Architecture = [string]$env:PROCESSOR_ARCHITECTURE
    IsOnBattery = [bool]$isOnBattery
    IsTpmPresent = [bool]($null -ne $tpm)
    SystemFirmwareHardwareId = [string]$systemFirmwareHardwareId
    PnpDevices = $pnpDevices
} | ConvertTo-Json -Compress -Depth 8
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, Path.GetTempPath(), cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogWarning("Hardware profile detection returned no data. Using fallback profile. ExitCode={ExitCode}", execution.ExitCode);
            return BuildFallbackProfile();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(execution.StandardOutput);
            JsonElement root = document.RootElement;

            string manufacturer = ReadProperty(root, "Manufacturer");
            string model = ReadProperty(root, "Model");
            string product = ReadProperty(root, "Product");
            string serial = ReadProperty(root, "SerialNumber");
            string architecture = NormalizeArchitecture(ReadProperty(root, "Architecture"));
            bool isVirtualMachine = IsVirtualMachine(manufacturer, model, product);
            bool isOnBattery = ReadBoolProperty(root, "IsOnBattery");
            bool isTpmPresent = ReadBoolProperty(root, "IsTpmPresent");
            string systemFirmwareHardwareId = ReadProperty(root, "SystemFirmwareHardwareId");
            IReadOnlyList<PnpDeviceInfo> pnpDevices = ReadPnpDevices(root);

            HardwareProfile profile = new()
            {
                Manufacturer = NormalizeManufacturer(manufacturer),
                Model = NormalizeValue(model),
                Product = NormalizeValue(product),
                SerialNumber = NormalizeValue(serial),
                Architecture = architecture,
                IsVirtualMachine = isVirtualMachine,
                IsOnBattery = isOnBattery,
                IsTpmPresent = isTpmPresent,
                SystemFirmwareHardwareId = systemFirmwareHardwareId.Trim(),
                PnpDevices = pnpDevices
            };

            _logger.LogInformation("Hardware profile detected. Manufacturer={Manufacturer}, Model={Model}, Architecture={Architecture}, IsVirtualMachine={IsVirtualMachine}, IsOnBattery={IsOnBattery}, IsTpmPresent={IsTpmPresent}",
                profile.Manufacturer,
                profile.Model,
                profile.Architecture,
                profile.IsVirtualMachine,
                profile.IsOnBattery,
                profile.IsTpmPresent);
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse hardware profile payload. Falling back to default profile.");
            return BuildFallbackProfile();
        }
    }

    private static HardwareProfile BuildFallbackProfile()
    {
        string architecture = NormalizeArchitecture(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty);
        return new HardwareProfile
        {
            Manufacturer = "Unknown",
            Model = "Unknown",
            Product = "Unknown",
            SerialNumber = "Unknown",
            Architecture = architecture,
            IsVirtualMachine = false,
            IsOnBattery = false,
            IsTpmPresent = false,
            SystemFirmwareHardwareId = string.Empty,
            PnpDevices = Array.Empty<PnpDeviceInfo>()
        };
    }

    private static string ReadProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool ReadBoolProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed) && parsed);
    }

    private static IReadOnlyList<PnpDeviceInfo> ReadPnpDevices(JsonElement root)
    {
        if (!root.TryGetProperty("PnpDevices", out JsonElement devicesElement) ||
            devicesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PnpDeviceInfo>();
        }

        var devices = new List<PnpDeviceInfo>();
        foreach (JsonElement deviceElement in devicesElement.EnumerateArray())
        {
            if (deviceElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            devices.Add(new PnpDeviceInfo
            {
                Name = ReadProperty(deviceElement, "Name"),
                DeviceId = ReadProperty(deviceElement, "DeviceId"),
                HardwareIds = ReadStringArrayProperty(deviceElement, "HardwareIds"),
                ClassGuid = ReadProperty(deviceElement, "ClassGuid"),
                Manufacturer = ReadProperty(deviceElement, "Manufacturer"),
                PnpClass = ReadProperty(deviceElement, "PnpClass")
            });
        }

        return devices;
    }

    private static IReadOnlyList<string> ReadStringArrayProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string? stringValue = value.GetString();
            return string.IsNullOrWhiteSpace(stringValue)
                ? Array.Empty<string>()
                : [stringValue.Trim()];
        }

        return Array.Empty<string>();
    }

    private static bool IsVirtualMachine(string manufacturer, string model, string product)
    {
        string combined = string.Join(" | ", manufacturer, model, product).ToLowerInvariant();

        if (combined.Contains("vmware") ||
            combined.Contains("virtualbox") ||
            combined.Contains("virtual machine") ||
            combined.Contains("kvm") ||
            combined.Contains("qemu") ||
            combined.Contains("xen") ||
            combined.Contains("hvm domu") ||
            combined.Contains("parallels") ||
            combined.Contains("bhyve"))
        {
            return true;
        }

        return combined.Contains("microsoft corporation") && combined.Contains("virtual");
    }

    private static string NormalizeManufacturer(string value)
    {
        string normalized = NormalizeValue(value);
        if (normalized.Contains("Hewlett", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("HP", StringComparison.OrdinalIgnoreCase))
        {
            return "HP";
        }

        if (normalized.Contains("Dell", StringComparison.OrdinalIgnoreCase))
        {
            return "Dell";
        }

        if (normalized.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return "Lenovo";
        }

        if (normalized.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft";
        }

        return normalized;
    }

    private static string NormalizeArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }

    private static string NormalizeValue(string value)
    {
        string normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Unknown" : normalized;
    }
}

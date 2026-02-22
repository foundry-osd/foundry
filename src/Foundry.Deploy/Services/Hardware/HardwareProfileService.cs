using System.Text.Json;
using System.Text;
using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Hardware;

public sealed class HardwareProfileService : IHardwareProfileService
{
    private readonly IProcessRunner _processRunner;

    public HardwareProfileService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        string script = @"
$computer = Get-CimInstance -ClassName Win32_ComputerSystem
$product = Get-CimInstance -ClassName Win32_ComputerSystemProduct
$bios = Get-CimInstance -ClassName Win32_BIOS
$tpm = Get-CimInstance -Namespace 'ROOT\cimv2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction SilentlyContinue

[pscustomobject]@{
    Manufacturer = [string]$computer.Manufacturer
    Model = [string]$computer.Model
    Product = [string]$product.Version
    SerialNumber = [string]$bios.SerialNumber
    Architecture = [string]$env:PROCESSOR_ARCHITECTURE
    IsTpmPresent = [bool]($null -ne $tpm)
} | ConvertTo-Json -Compress
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, Path.GetTempPath(), cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
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
            bool isTpmPresent = ReadBoolProperty(root, "IsTpmPresent");

            bool isAutopilotCapable =
                !string.IsNullOrWhiteSpace(serial) &&
                !string.IsNullOrWhiteSpace(manufacturer) &&
                !string.IsNullOrWhiteSpace(model) &&
                isTpmPresent;

            return new HardwareProfile
            {
                Manufacturer = NormalizeManufacturer(manufacturer),
                Model = NormalizeValue(model),
                Product = NormalizeValue(product),
                SerialNumber = NormalizeValue(serial),
                Architecture = architecture,
                IsTpmPresent = isTpmPresent,
                IsAutopilotCapable = isAutopilotCapable
            };
        }
        catch
        {
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
            IsTpmPresent = false,
            IsAutopilotCapable = false
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

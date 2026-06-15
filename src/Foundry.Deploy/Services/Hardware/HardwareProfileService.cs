using System.Text.RegularExpressions;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Hardware;

public sealed partial class HardwareProfileService : IHardwareProfileService
{
    private const string SystemFirmwareClassGuid = "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}";

    private readonly IHardwareProfileSource _source;
    private readonly ILogger<HardwareProfileService> _logger;
    private readonly Func<string> _architectureProvider;

    public HardwareProfileService(ILogger<HardwareProfileService> logger)
        : this(new WindowsHardwareProfileSource(), logger, () => Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty)
    {
    }

    internal HardwareProfileService(
        IHardwareProfileSource source,
        ILogger<HardwareProfileService> logger,
        Func<string> architectureProvider)
    {
        _source = source;
        _logger = logger;
        _architectureProvider = architectureProvider;
    }

    public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Detecting current hardware profile.");

        try
        {
            HardwareProfileSnapshot snapshot = _source.Capture();
            HardwareProfile profile = BuildProfile(snapshot);

            _logger.LogInformation("Hardware profile detected. Manufacturer={Manufacturer}, Model={Model}, Architecture={Architecture}, IsVirtualMachine={IsVirtualMachine}, IsOnBattery={IsOnBattery}, IsTpmPresent={IsTpmPresent}",
                profile.Manufacturer,
                profile.Model,
                profile.Architecture,
                profile.IsVirtualMachine,
                profile.IsOnBattery,
                profile.IsTpmPresent);

            return Task.FromResult(profile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native hardware profile detection failed. Using fallback hardware profile.");
            return Task.FromResult(BuildFallbackProfile());
        }
    }

    private HardwareProfile BuildProfile(HardwareProfileSnapshot snapshot)
    {
        string manufacturer = NormalizeManufacturer(snapshot.Manufacturer);
        string model = NormalizeValue(snapshot.Model);
        string product = NormalizeValue(snapshot.Product);
        string serial = NormalizeValue(snapshot.SerialNumber);
        IReadOnlyList<PnpDeviceInfo> devices = snapshot.Devices;

        return new HardwareProfile
        {
            Manufacturer = manufacturer,
            Model = model,
            Product = product,
            SerialNumber = serial,
            Architecture = NormalizeArchitecture(_architectureProvider()),
            IsVirtualMachine = IsVirtualMachine(manufacturer, model, product),
            IsOnBattery = snapshot.IsOnBattery,
            IsTpmPresent = HasTpmDevice(devices),
            SystemFirmwareHardwareId = ResolveSystemFirmwareHardwareId(devices),
            PnpDevices = devices
        };
    }

    private HardwareProfile BuildFallbackProfile()
    {
        return new HardwareProfile
        {
            Manufacturer = "Unknown",
            Model = "Unknown",
            Product = "Unknown",
            SerialNumber = "Unknown",
            Architecture = NormalizeArchitecture(_architectureProvider()),
            IsVirtualMachine = false,
            IsOnBattery = false,
            IsTpmPresent = false,
            SystemFirmwareHardwareId = string.Empty,
            PnpDevices = Array.Empty<PnpDeviceInfo>()
        };
    }

    private static string ResolveSystemFirmwareHardwareId(IReadOnlyList<PnpDeviceInfo> devices)
    {
        PnpDeviceInfo? firmwareDevice = devices.FirstOrDefault(device =>
            string.Equals(device.ClassGuid, SystemFirmwareClassGuid, StringComparison.OrdinalIgnoreCase));

        if (firmwareDevice is null)
        {
            return string.Empty;
        }

        Match match = FirmwareHardwareIdRegex().Match(firmwareDevice.DeviceId);
        if (!match.Success)
        {
            match = firmwareDevice.HardwareIds
                .Select(id => FirmwareHardwareIdRegex().Match(id))
                .FirstOrDefault(candidate => candidate.Success) ?? Match.Empty;
        }

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static bool HasTpmDevice(IReadOnlyList<PnpDeviceInfo> devices)
    {
        return devices.Any(device =>
            device.HardwareIds.Any(id => id.Contains("MSFT0101", StringComparison.OrdinalIgnoreCase)) ||
            device.Name.Contains("Trusted Platform Module", StringComparison.OrdinalIgnoreCase) ||
            device.PnpClass.Contains("SecurityDevices", StringComparison.OrdinalIgnoreCase));
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

    [GeneratedRegex(@"\{?(([0-9a-f]){8}-([0-9a-f]){4}-([0-9a-f]){4}-([0-9a-f]){4}-([0-9a-f]){12})\}?", RegexOptions.IgnoreCase)]
    private static partial Regex FirmwareHardwareIdRegex();
}

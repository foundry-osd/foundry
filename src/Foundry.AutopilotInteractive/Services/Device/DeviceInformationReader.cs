// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Management;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Services.Device;

/// <summary>
/// Reads the hardware identity shown in the footer.
/// </summary>
public interface IDeviceInformationReader
{
    /// <summary>
    /// Reads manufacturer, model and serial number. Never throws: a value that cannot be read is <see langword="null"/>.
    /// </summary>
    /// <returns>The device identity.</returns>
    DeviceInformation Read();
}

/// <summary>
/// Reads <see cref="DeviceInformation"/> from WMI (<c>Win32_ComputerSystem</c> and <c>Win32_BIOS</c>).
/// </summary>
/// <remarks>
/// Each class is queried separately so one failing provider does not hide the other values. Values are not logged: the
/// serial number identifies the device.
/// </remarks>
public sealed class DeviceInformationReader : IDeviceInformationReader
{
    private readonly ILogger<DeviceInformationReader> _logger;

    /// <param name="logger">Receives a warning for each WMI class that cannot be read.</param>
    public DeviceInformationReader(ILogger<DeviceInformationReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DeviceInformation Read()
    {
        (string? manufacturer, string? model) = ReadComputerSystem();
        string? serialNumber = ReadSerialNumber();

        _logger.LogDebug(
            "Device information read. HasManufacturer={HasManufacturer}, HasModel={HasModel}, HasSerialNumber={HasSerialNumber}",
            manufacturer is not null,
            model is not null,
            serialNumber is not null);

        return new DeviceInformation(manufacturer, model, serialNumber);
    }

    private (string? Manufacturer, string? Model) ReadComputerSystem()
    {
        try
        {
            using ManagementObjectSearcher searcher = new("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementBaseObject instance in searcher.Get())
            {
                using (instance)
                {
                    return (ReadText(instance, "Manufacturer"), ReadText(instance, "Model"));
                }
            }
        }
        catch (Exception exception)
        {
            LogReadFailure("Win32_ComputerSystem", exception);
        }

        return (null, null);
    }

    private string? ReadSerialNumber()
    {
        try
        {
            using ManagementObjectSearcher searcher = new("SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementBaseObject instance in searcher.Get())
            {
                using (instance)
                {
                    return ReadText(instance, "SerialNumber");
                }
            }
        }
        catch (Exception exception)
        {
            LogReadFailure("Win32_BIOS", exception);
        }

        return null;
    }

    private static string? ReadText(ManagementBaseObject instance, string propertyName)
    {
        string? value = instance[propertyName] as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void LogReadFailure(string wmiClass, Exception exception)
    {
        _logger.LogWarning(
            "Device information could not be read from WMI. WmiClass={WmiClass}, FailureType={FailureType}, ErrorCode={ErrorCode}",
            wmiClass,
            exception.GetType().Name,
            exception.HResult);
    }
}

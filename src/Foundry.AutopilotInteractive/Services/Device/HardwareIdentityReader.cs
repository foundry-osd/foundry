// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Management;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Services.Device;

/// <summary>
/// The identity Windows Autopilot needs to register this device.
/// </summary>
/// <param name="SerialNumber">BIOS serial number.</param>
/// <param name="HardwareHash">Base64 hardware hash (4K hash) of the device.</param>
public sealed record HardwareIdentity(string SerialNumber, string HardwareHash);

/// <summary>
/// Raised when the serial number or the hardware hash of the device cannot be read.
/// </summary>
public sealed class HardwareIdentityUnavailableException : Exception
{
    /// <param name="message">Non-sensitive description of what is missing.</param>
    public HardwareIdentityUnavailableException(string message)
        : base(message)
    {
    }

    /// <param name="message">Non-sensitive description of what is missing.</param>
    /// <param name="innerException">The WMI failure.</param>
    public HardwareIdentityUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads the hardware identity used for Autopilot registration.
/// </summary>
public interface IHardwareIdentityReader
{
    /// <summary>
    /// Reads the serial number and the hardware hash.
    /// </summary>
    /// <returns>The identity, with both values non-blank.</returns>
    /// <exception cref="HardwareIdentityUnavailableException">The hash or the serial number is blank or cannot be read.</exception>
    HardwareIdentity Read();
}

/// <summary>
/// Reads the hardware hash from the MDM WMI bridge and the serial number from <c>Win32_BIOS</c>.
/// </summary>
/// <remarks>
/// The hash is only exposed while Windows runs in a context that can reach <c>root/cimv2/mdm/dmmap</c>, which is the case
/// during the specialize pass. Neither value is logged: both identify the device.
/// </remarks>
public sealed class HardwareIdentityReader : IHardwareIdentityReader
{
    private const string HashNamespace = @"root\cimv2\mdm\dmmap";
    private const string HashQuery = "SELECT DeviceHardwareData FROM MDM_DevDetail_Ext01 WHERE InstanceID='Ext' AND ParentID='./DevDetail'";
    private const string SerialQuery = "SELECT SerialNumber FROM Win32_BIOS";

    private readonly ILogger<HardwareIdentityReader> _logger;

    /// <param name="logger">Receives a warning for each WMI class that cannot be read.</param>
    public HardwareIdentityReader(ILogger<HardwareIdentityReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public HardwareIdentity Read()
    {
        string? serialNumber = ReadFirstText(null, SerialQuery, "SerialNumber", "Win32_BIOS");
        string? hardwareHash = ReadFirstText(HashNamespace, HashQuery, "DeviceHardwareData", "MDM_DevDetail_Ext01");

        if (string.IsNullOrWhiteSpace(serialNumber) || string.IsNullOrWhiteSpace(hardwareHash))
        {
            throw new HardwareIdentityUnavailableException(
                $"The hardware identity is incomplete. HasSerialNumber={serialNumber is not null}, HasHardwareHash={hardwareHash is not null}.");
        }

        return new HardwareIdentity(serialNumber, hardwareHash);
    }

    private string? ReadFirstText(string? wmiNamespace, string query, string propertyName, string wmiClass)
    {
        try
        {
            using ManagementObjectSearcher searcher = wmiNamespace is null
                ? new ManagementObjectSearcher(query)
                : new ManagementObjectSearcher(wmiNamespace, query);
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject instance in results)
            {
                using (instance)
                {
                    string? value = instance[propertyName] as string;
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Hardware identity could not be read from WMI. WmiClass={WmiClass}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                wmiClass,
                exception.GetType().Name,
                exception.HResult);
        }

        return null;
    }
}

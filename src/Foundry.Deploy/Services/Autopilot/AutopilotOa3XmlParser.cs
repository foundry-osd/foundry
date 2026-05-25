using System.Xml.Linq;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Parses the OA3Tool hardware report produced in WinPE.
/// </summary>
public static class AutopilotOa3XmlParser
{
    /// <summary>
    /// Parses the required serial number and hardware hash from OA3.xml content.
    /// </summary>
    /// <param name="xml">OA3 report XML content.</param>
    /// <returns>A structured parser result.</returns>
    public static AutopilotHardwareHashParseResult Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, "OA3 report is empty.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception ex) when (ex is global::System.Xml.XmlException or InvalidOperationException)
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, $"OA3 report XML is invalid: {ex.Message}");
        }

        string serialNumber = FindValue(document, "SerialNumber", "DeviceSerialNumber", "Serial");
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.SerialMissing, "OA3 report does not contain a serial number.");
        }

        string hardwareHash = FindValue(document, "HardwareHash", "HardwareIdentifier", "HardwareId");
        if (string.IsNullOrWhiteSpace(hardwareHash))
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.HashMissing, "OA3 report does not contain a hardware hash.");
        }

        return new AutopilotHardwareHashParseResult
        {
            FailureCode = AutopilotHardwareHashCaptureFailureCode.None,
            Identity = new AutopilotHardwareHashDeviceIdentity(serialNumber.Trim(), hardwareHash.Trim(), null),
            Message = "OA3 report parsed."
        };
    }

    private static AutopilotHardwareHashParseResult Failed(
        AutopilotHardwareHashCaptureFailureCode failureCode,
        string message)
    {
        return new AutopilotHardwareHashParseResult
        {
            FailureCode = failureCode,
            Message = message
        };
    }

    private static string FindValue(XDocument document, params string[] localNames)
    {
        foreach (string localName in localNames)
        {
            string? value = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}

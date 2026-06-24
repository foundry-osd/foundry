// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Parses the OA3Tool hardware report produced in WinPE.
/// </summary>
public static class AutopilotOa3XmlParser
{
    /// <summary>
    /// Parses the required serial number and hardware hash from OA3 output content.
    /// </summary>
    /// <param name="xml">OA3 import XML content.</param>
    /// <param name="traceXml">Optional OA3 trace XML content containing hardware verification details.</param>
    /// <returns>A structured parser result.</returns>
    public static AutopilotHardwareHashParseResult Parse(string xml, string? traceXml = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, "OA3 report is empty.");
        }

        if (!TryParseDocument(xml, "OA3 report", out XDocument? parsedImportDocument, out AutopilotHardwareHashParseResult? importFailure))
        {
            return importFailure!;
        }

        XDocument importDocument = parsedImportDocument!;
        XDocument? traceDocument = null;
        if (!string.IsNullOrWhiteSpace(traceXml))
        {
            _ = TryParseDocument(traceXml, "OA3 trace report", out traceDocument, out _);
        }

        string serialNumber = FindValue(importDocument, "SerialNumber", "DeviceSerialNumber", "Serial");
        if (string.IsNullOrWhiteSpace(serialNumber) && traceDocument is not null)
        {
            serialNumber = FindValue(traceDocument, "SerialNumber", "DeviceSerialNumber", "Serial");
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return Failed(AutopilotHardwareHashCaptureFailureCode.SerialMissing, "OA3 report does not contain a serial number.");
        }

        string hardwareHash = FindValue(importDocument, "HardwareHash", "HardwareIdentifier", "HardwareId");
        if (string.IsNullOrWhiteSpace(hardwareHash) && traceDocument is not null)
        {
            hardwareHash = FindValue(traceDocument, "HardwareHash", "HardwareIdentifier", "HardwareId");
        }

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

    private static bool TryParseDocument(
        string xml,
        string documentName,
        out XDocument? document,
        out AutopilotHardwareHashParseResult? failure)
    {
        try
        {
            document = XDocument.Parse(xml);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is global::System.Xml.XmlException or InvalidOperationException)
        {
            document = null;
            failure = Failed(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, $"{documentName} XML is invalid: {ex.Message}");
            return false;
        }
    }

    private static string FindValue(XDocument document, params string[] localNames)
    {
        foreach (string localName in localNames)
        {
            string? value = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (IsUsableValue(value))
            {
                return value!;
            }

            value = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Attribute("name")?.Value.Equals(localName, StringComparison.OrdinalIgnoreCase) == true)
                ?.Value;

            if (IsUsableValue(value))
            {
                return value!;
            }
        }

        return string.Empty;
    }

    private static bool IsUsableValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return !trimmed.Equals("None", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase);
    }
}

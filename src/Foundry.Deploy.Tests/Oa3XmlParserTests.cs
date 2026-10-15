// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class Oa3XmlParserTests
{
    [Fact]
    public void Parse_WhenOa3XmlIsValid_ReturnsSerialNumberAndHardwareHash()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <Key>
              <SerialNumber>ABC123</SerialNumber>
              <HardwareHash>HASHVALUE</HardwareHash>
            </Key>
            """);

        Assert.True(result.IsSuccess);
        Assert.Equal("ABC123", result.Identity?.SerialNumber);
        Assert.Equal("HASHVALUE", result.Identity?.HardwareHash);
    }

    [Fact]
    public void Parse_WhenSerialNumberIsOnlyInTraceReport_ReturnsSerialNumberAndHardwareHash()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse(
            """
            <Key>
              <ProductKeyState>6</ProductKeyState>
              <HardwareHash>HASHVALUE</HardwareHash>
            </Key>
            """,
            """
            <HardwareVerificationData>
              <Hardware>
                <SMBIOS>
                  <System>
                    <p name="Manufacturer">VMware, Inc.</p>
                    <p name="SerialNumber">SER123</p>
                  </System>
                </SMBIOS>
              </Hardware>
            </HardwareVerificationData>
            """);

        Assert.True(result.IsSuccess);
        Assert.Equal("SER123", result.Identity?.SerialNumber);
        Assert.Equal("HASHVALUE", result.Identity?.HardwareHash);
    }

    [Fact]
    public void Parse_WhenHardwareHashIsMissing_ReturnsHashMissingFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("""
            <Key>
              <SerialNumber>ABC123</SerialNumber>
            </Key>
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.HashMissing, result.FailureCode);
    }

    [Fact]
    public void Parse_WhenSerialNumberIsMissing_ReturnsSerialMissingFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("""
            <Key>
              <HardwareHash>HASHVALUE</HardwareHash>
            </Key>
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.SerialMissing, result.FailureCode);
    }

    [Fact]
    public void Parse_WhenTpmIsDetectedButVersionCouldNotBeRead_ReturnsTpmVersionMissingFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse(
            """
            <Key>
              <SerialNumber>ABC123</SerialNumber>
              <HardwareHash>HASHVALUE</HardwareHash>
            </Key>
            """,
            CreateTraceWithTpm("""<p name="TPM Version" error="0xC0000225"/>"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.TpmVersionMissing, result.FailureCode);
        Assert.Contains("0xC0000225", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenTpmVersionIsReported_ReturnsSerialNumberAndHardwareHash()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse(
            """
            <Key>
              <SerialNumber>ABC123</SerialNumber>
              <HardwareHash>HASHVALUE</HardwareHash>
            </Key>
            """,
            CreateTraceWithTpm("""<p name="TPM Version">TPM-Version:2.0 -Level:0-Revision:1.38</p>"""));

        Assert.True(result.IsSuccess);
        Assert.Equal("HASHVALUE", result.Identity?.HardwareHash);
    }

    [Fact]
    public void Parse_WhenXmlIsInvalid_ReturnsReportInvalidFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("<Key>");

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, result.FailureCode);
    }

    private static string CreateTraceWithTpm(string tpmVersionElement)
    {
        return $"""
            <HardwareVerificationData>
              <Identity>
                <TPM>
                  {tpmVersionElement}
                  <p name="TPM EkPub">EKPUBVALUE</p>
                </TPM>
              </Identity>
            </HardwareVerificationData>
            """;
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class Oa3XmlParserTests
{
    [Fact]
    public void Parse_RejectsInvalidBase64WithoutTreatingSyntaxAsQuality()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("<Key><SerialNumber>SER123</SerialNumber><HardwareHash>not-base64!</HardwareHash></Key>");
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, result.FailureCode);
    }

    [Fact]
    public void Parse_RejectsDtdAndOversizedReport()
    {
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid,
            AutopilotOa3XmlParser.Parse("<!DOCTYPE Key [<!ENTITY value 'data'>]><Key>&value;</Key>").FailureCode);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid,
            AutopilotOa3XmlParser.Parse("<Key>" + new string('x', 1024 * 1024) + "</Key>").FailureCode);
    }
    [Fact]
    public void Parse_WhenOa3XmlIsValid_ReturnsSerialNumberAndHardwareHash()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <Key>
              <SerialNumber>ABC123</SerialNumber>
              <HardwareHash>SEFTSFZBTFVF</HardwareHash>
            </Key>
            """);

        Assert.True(result.IsSuccess);
        Assert.Equal("ABC123", result.Identity?.SerialNumber);
        Assert.Equal("SEFTSFZBTFVF", result.Identity?.HardwareHash);
    }

    [Fact]
    public void Parse_WhenSerialNumberIsOnlyInTraceReport_ReturnsSerialNumberAndHardwareHash()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse(
            """
            <Key>
              <ProductKeyState>6</ProductKeyState>
              <HardwareHash>SEFTSFZBTFVF</HardwareHash>
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
        Assert.Equal("SEFTSFZBTFVF", result.Identity?.HardwareHash);
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
              <HardwareHash>SEFTSFZBTFVF</HardwareHash>
            </Key>
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.SerialMissing, result.FailureCode);
    }

    [Fact]
    public void Parse_WhenXmlIsInvalid_ReturnsReportInvalidFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("<Key>");

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, result.FailureCode);
    }
}

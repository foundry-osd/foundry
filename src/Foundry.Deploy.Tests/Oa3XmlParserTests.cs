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
    public void Parse_WhenXmlIsInvalid_ReturnsReportInvalidFailure()
    {
        AutopilotHardwareHashParseResult result = AutopilotOa3XmlParser.Parse("<Key>");

        Assert.False(result.IsSuccess);
        Assert.Equal(AutopilotHardwareHashCaptureFailureCode.ReportInvalid, result.FailureCode);
    }
}

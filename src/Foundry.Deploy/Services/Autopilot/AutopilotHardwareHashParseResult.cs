namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Represents OA3 XML parsing output without retaining the full report in memory longer than needed.
/// </summary>
public sealed record AutopilotHardwareHashParseResult
{
    /// <summary>
    /// Gets whether the OA3 report contained the required values.
    /// </summary>
    public bool IsSuccess => FailureCode == AutopilotHardwareHashCaptureFailureCode.None;

    /// <summary>
    /// Gets the parser failure code.
    /// </summary>
    public AutopilotHardwareHashCaptureFailureCode FailureCode { get; init; }

    /// <summary>
    /// Gets the parsed identity when the report is valid.
    /// </summary>
    public AutopilotHardwareHashDeviceIdentity? Identity { get; init; }

    /// <summary>
    /// Gets a sanitized parser message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

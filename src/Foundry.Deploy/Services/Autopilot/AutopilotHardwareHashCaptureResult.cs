namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Represents the sanitized result of the WinPE Autopilot hardware hash capture stage.
/// </summary>
public sealed record AutopilotHardwareHashCaptureResult
{
    /// <summary>
    /// Gets whether OA3Tool produced a usable hardware hash report.
    /// </summary>
    public bool IsSuccess => FailureCode == AutopilotHardwareHashCaptureFailureCode.None;

    /// <summary>
    /// Gets the structured failure code when capture did not complete.
    /// </summary>
    public AutopilotHardwareHashCaptureFailureCode FailureCode { get; init; }

    /// <summary>
    /// Gets the operator-facing capture status message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the captured device identity when capture succeeds.
    /// </summary>
    public AutopilotHardwareHashDeviceIdentity? Identity { get; init; }

    /// <summary>
    /// Gets the retained OA3 XML path when available.
    /// </summary>
    public string? Oa3XmlPath { get; init; }

    /// <summary>
    /// Gets the retained OA3 log path when available.
    /// </summary>
    public string? Oa3LogPath { get; init; }

    /// <summary>
    /// Gets the retained CSV path when available.
    /// </summary>
    public string? CsvPath { get; init; }

    /// <summary>
    /// Creates a successful capture result.
    /// </summary>
    public static AutopilotHardwareHashCaptureResult Succeeded(
        AutopilotHardwareHashDeviceIdentity identity,
        string oa3XmlPath,
        string? oa3LogPath,
        string csvPath)
    {
        return new AutopilotHardwareHashCaptureResult
        {
            FailureCode = AutopilotHardwareHashCaptureFailureCode.None,
            Message = "Autopilot hardware hash captured.",
            Identity = identity,
            Oa3XmlPath = oa3XmlPath,
            Oa3LogPath = oa3LogPath,
            CsvPath = csvPath
        };
    }

    /// <summary>
    /// Creates a failed capture result with no secret material.
    /// </summary>
    public static AutopilotHardwareHashCaptureResult Failed(
        AutopilotHardwareHashCaptureFailureCode failureCode,
        string message)
    {
        return new AutopilotHardwareHashCaptureResult
        {
            FailureCode = failureCode,
            Message = message
        };
    }
}

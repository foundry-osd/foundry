namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Provides the file-system roots required to capture an Autopilot hardware hash in WinPE.
/// </summary>
public sealed record AutopilotHardwareHashCaptureRequest
{
    /// <summary>
    /// Gets the root of the applied offline Windows image.
    /// </summary>
    public string TargetWindowsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the active WinPE Windows root that receives PCPKsp.dll before OA3Tool runs.
    /// </summary>
    public string WinPeWindowsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the active Foundry runtime root, normally X:\Foundry.
    /// </summary>
    public string WorkspaceRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the retained diagnostic artifact folder under the applied Windows image.
    /// </summary>
    public string DiagnosticsRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional group tag selected by the operator.
    /// </summary>
    public string? GroupTag { get; init; }
}

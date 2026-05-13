namespace Foundry.Telemetry;

/// <summary>
/// Resolves the telemetry boot media target from the explicit WinPE deployment mode.
/// </summary>
public static class TelemetryBootMediaTargetResolver
{
    /// <summary>
    /// Converts the resolved runtime mode into a low-cardinality telemetry value.
    /// </summary>
    /// <param name="runtime">Current telemetry runtime category.</param>
    /// <param name="deploymentMode">Explicit value of FOUNDRY_DEPLOYMENT_MODE when running in WinPE.</param>
    /// <returns>A stable boot media target value.</returns>
    public static string Resolve(string runtime, string? deploymentMode)
    {
        if (!string.Equals(runtime, TelemetryRuntimeModes.WinPe, StringComparison.Ordinal))
        {
            return TelemetryBootMediaTargets.None;
        }

        return deploymentMode?.Trim().ToLowerInvariant() switch
        {
            "iso" => TelemetryBootMediaTargets.Iso,
            "usb" => TelemetryBootMediaTargets.Usb,
            _ => TelemetryBootMediaTargets.Unknown
        };
    }
}

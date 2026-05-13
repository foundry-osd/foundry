namespace Foundry.Telemetry;

/// <summary>
/// Resolves the compile-time build configuration of the running assembly.
/// </summary>
public static class TelemetryBuildConfiguration
{
    /// <summary>
    /// Gets the stable telemetry value for the current build configuration.
    /// </summary>
    public static string Current
    {
        get
        {
#if DEBUG
            return "debug";
#else
            return "release";
#endif
        }
    }
}

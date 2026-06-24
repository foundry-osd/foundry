// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

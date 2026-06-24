// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Defines stable runtime categories used by telemetry events.
/// </summary>
public static class TelemetryRuntimeModes
{
    /// <summary>
    /// Represents the regular desktop Foundry OSD runtime.
    /// </summary>
    public const string Desktop = "desktop";

    /// <summary>
    /// Represents execution inside the WinPE runtime.
    /// </summary>
    public const string WinPe = "winpe";

    /// <summary>
    /// Represents a runtime that cannot be classified safely.
    /// </summary>
    public const string Unknown = "unknown";
}

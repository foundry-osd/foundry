// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Defines stable boot media target categories used by OSD and WinPE runtimes.
/// </summary>
public static class TelemetryBootMediaTargets
{
    public const string Iso = "iso";
    public const string Usb = "usb";
    public const string None = "none";
    public const string Unknown = "unknown";
}

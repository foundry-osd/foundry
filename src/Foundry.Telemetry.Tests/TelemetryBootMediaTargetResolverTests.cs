// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryBootMediaTargetResolverTests
{
    [Theory]
    [InlineData(TelemetryRuntimeModes.Desktop, null, TelemetryBootMediaTargets.None)]
    [InlineData(TelemetryRuntimeModes.Desktop, "Usb", TelemetryBootMediaTargets.None)]
    [InlineData(TelemetryRuntimeModes.WinPe, "Usb", TelemetryBootMediaTargets.Usb)]
    [InlineData(TelemetryRuntimeModes.WinPe, "Iso", TelemetryBootMediaTargets.Iso)]
    [InlineData(TelemetryRuntimeModes.WinPe, "usb", TelemetryBootMediaTargets.Usb)]
    [InlineData(TelemetryRuntimeModes.WinPe, "iso", TelemetryBootMediaTargets.Iso)]
    [InlineData(TelemetryRuntimeModes.WinPe, null, TelemetryBootMediaTargets.Unknown)]
    [InlineData(TelemetryRuntimeModes.WinPe, "", TelemetryBootMediaTargets.Unknown)]
    [InlineData(TelemetryRuntimeModes.WinPe, "Foundry Cache", TelemetryBootMediaTargets.Unknown)]
    public void Resolve_ReturnsTargetFromExplicitRuntimeModeOnly(string runtime, string? deploymentMode, string expected)
    {
        Assert.Equal(expected, TelemetryBootMediaTargetResolver.Resolve(runtime, deploymentMode));
    }
}

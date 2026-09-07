// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotCaptureCapabilityEvaluatorTests
{
    [Theory]
    [InlineData(true, WirelessAdapterPresence.Present, true, true, "full_windows_capture_required")]
    [InlineData(true, WirelessAdapterPresence.Unknown, true, true, "unqualified_capture_environment")]
    [InlineData(true, WirelessAdapterPresence.Absent, false, true, "unqualified_capture_environment")]
    [InlineData(true, WirelessAdapterPresence.Absent, true, false, "unqualified_capture_environment")]
    [InlineData(false, WirelessAdapterPresence.Absent, true, true, "full_windows_capture_required")]
    public void UnqualifiedOrRestrictedCaptureCannotRunOa3InPe(bool isWinPe, WirelessAdapterPresence wireless,
        bool tools, bool hardware, string code)
    {
        AutopilotCaptureCapability result = AutopilotCaptureCapabilityEvaluator.Evaluate(isWinPe, wireless, tools, hardware);
        Assert.False(result.CanCaptureInWinPe);
        Assert.True(result.RequiresFullWindows);
        Assert.Equal(code, result.ReasonCode);
    }

    [Fact]
    public void QualifiedWiredPeIsEligibleButDoesNotProveHashQuality()
    {
        AutopilotCaptureCapability result = AutopilotCaptureCapabilityEvaluator.Evaluate(true, WirelessAdapterPresence.Absent, true, true);
        Assert.True(result.CanCaptureInWinPe);
        Assert.False(result.RequiresFullWindows);
        Assert.Equal("qualified_winpe_capture", result.ReasonCode);
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>Unknown does not establish that an internal wireless adapter is absent.</summary>
public enum WirelessAdapterPresence { Unknown, Absent, Present }

/// <summary>Separates execution eligibility from syntax and hardware-hash quality.</summary>
public sealed record AutopilotCaptureCapability(bool CanCaptureInWinPe, bool RequiresFullWindows, string ReasonCode);

/// <summary>Applies the documented OA3 wireless restriction and requires explicit qualification evidence.</summary>
public static class AutopilotCaptureCapabilityEvaluator
{
    public static AutopilotCaptureCapability Evaluate(bool isWinPe, WirelessAdapterPresence internalWireless,
        bool qualifiedToolPair, bool qualifiedHardware)
    {
        if (!isWinPe || internalWireless == WirelessAdapterPresence.Present)
            return new(false, true, "full_windows_capture_required");
        if (internalWireless != WirelessAdapterPresence.Absent || !qualifiedToolPair || !qualifiedHardware)
            return new(false, true, "unqualified_capture_environment");
        return new(true, false, "qualified_winpe_capture");
    }
}

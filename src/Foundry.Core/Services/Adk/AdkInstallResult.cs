// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>Identifies the confirmed terminal state of an ADK operation.</summary>
public enum AdkInstallOutcome
{
    /// <summary>All required components are ready.</summary>
    Ready,
    /// <summary>A native stage requested a restart.</summary>
    RebootRequired,
    /// <summary>Required components remain unavailable.</summary>
    NotReady,
    /// <summary>The request stopped before further native stages.</summary>
    Cancelled,
    /// <summary>Native completion could not be confirmed.</summary>
    OwnershipUncertain
}

/// <summary>Combines actual readiness with native setup outcomes.</summary>
public sealed record AdkInstallResult(AdkInstallationStatus Status, AdkInstallOutcome Outcome, bool RebootRequired)
{
    /// <summary>Requires successful exit codes and actual readiness, preserving restart requests.</summary>
    public static AdkInstallResult Classify(AdkInstallationStatus status, IReadOnlyList<int> exitCodes)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(exitCodes);
        bool reboot = exitCodes.Any(code => code is 3010 or 1641);
        AdkInstallOutcome outcome = reboot ? AdkInstallOutcome.RebootRequired
            : exitCodes.Count > 0 && exitCodes.All(code => code == 0) && status.CanCreateMedia
                ? AdkInstallOutcome.Ready : AdkInstallOutcome.NotReady;
        return new(status, outcome, reboot);
    }
}

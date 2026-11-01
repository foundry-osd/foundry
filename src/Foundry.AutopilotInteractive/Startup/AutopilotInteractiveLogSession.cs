// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Autopilot;
using Foundry.Utilities.Diagnostics;

namespace Foundry.AutopilotInteractive.Startup;

/// <summary>
/// Chooses the diagnostic session identifier stamped on every log event.
/// </summary>
public static class AutopilotInteractiveLogSession
{
    /// <summary>
    /// Resolves the session identifier: the one in the runtime configuration, which Deploy stages so these logs correlate
    /// with the deployment, then the inherited environment value, then a generated one.
    /// </summary>
    /// <param name="configuration">Loaded configuration, or <see langword="null"/> when it could not be loaded.</param>
    /// <param name="environmentSessionId">Value of <c>FOUNDRY_DIAGNOSTIC_SESSION_ID</c>, or <see langword="null"/>.</param>
    /// <returns>A normalized, non-empty session identifier.</returns>
    public static string Resolve(AutopilotInteractiveRuntimeConfiguration? configuration, string? environmentSessionId)
    {
        string? candidate = string.IsNullOrWhiteSpace(configuration?.DiagnosticSessionId)
            ? environmentSessionId
            : configuration.DiagnosticSessionId;
        return DiagnosticSessionContext.ResolveSessionId(candidate);
    }
}

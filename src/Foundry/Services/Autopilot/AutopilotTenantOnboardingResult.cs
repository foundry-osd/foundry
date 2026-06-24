// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Represents the result of a tenant onboarding pass for the managed Autopilot app registration.
/// </summary>
public sealed record AutopilotTenantOnboardingResult
{
    /// <summary>
    /// Gets the updated persistent settings.
    /// </summary>
    public AutopilotHardwareHashUploadSettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the evaluated tenant registration status after Graph discovery and repair.
    /// </summary>
    public AutopilotTenantOnboardingStatus Status { get; init; }

    /// <summary>
    /// Gets the app registration certificate credentials discovered from Microsoft Graph.
    /// </summary>
    public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; init; } = [];
}

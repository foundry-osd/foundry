// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Identifies the qualified setup stage that launches a first-boot action set.</summary>
public enum FirstBootEntryPoint
{
    None,
    GeneratedSpecialize,
    VerifiedCustomSpecialize,
    SetupComplete,
    OobeCommand
}

/// <summary>Records qualified setup entry points before disk preparation; staging does not prove execution.</summary>
public sealed record FirstBootExecutionPlan(FirstBootEntryPoint CustomizationEntryPoint,
    bool CanLaunchInteractiveRegistration, string? FailureCode);

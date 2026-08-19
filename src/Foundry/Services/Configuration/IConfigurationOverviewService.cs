// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Evaluates the current user-facing configuration overview.
/// </summary>
public interface IConfigurationOverviewService
{
    /// <summary>
    /// Evaluates the current configuration and runtime-only readiness inputs.
    /// </summary>
    ConfigurationOverviewEvaluation Evaluate();
}

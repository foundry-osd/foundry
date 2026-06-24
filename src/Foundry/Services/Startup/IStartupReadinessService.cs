// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Startup;

/// <summary>
/// Runs startup checks that prime application state before the main workflow is used.
/// </summary>
public interface IStartupReadinessService
{
    /// <summary>
    /// Initializes startup readiness state, including ADK detection and shell navigation guards.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels startup checks.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

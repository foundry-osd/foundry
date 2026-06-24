// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Stores and broadcasts the current application update result for shell and settings views.
/// </summary>
public interface IApplicationUpdateStateService
{
    /// <summary>
    /// Occurs after a new update result is published.
    /// </summary>
    event EventHandler<ApplicationUpdateStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the latest published update result.
    /// </summary>
    ApplicationUpdateCheckResult? CurrentResult { get; }

    /// <summary>
    /// Publishes a new update result to subscribers.
    /// </summary>
    /// <param name="result">Result to store and broadcast.</param>
    void Publish(ApplicationUpdateCheckResult result);
}

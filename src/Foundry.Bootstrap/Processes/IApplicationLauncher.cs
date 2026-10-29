// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Processes;

/// <summary>Launches each application once; cancelling observation never terminates the child.</summary>
/// <remarks>
/// The optional deployment key hand-off callbacks run only once the process started: <c>processStarted</c> receives its
/// process id right after the start, and <c>processReleasing</c> is awaited on every later path (success, failure, legacy
/// handoff, cancellation) before the launcher disposes its <see cref="System.Diagnostics.Process"/>. The open process handle
/// keeps Windows from reusing the child's process id while <see cref="IDeploymentKeyRelay"/> still trusts it. A callback
/// failure is logged with its exception type only and never changes the launch result.
/// </remarks>
internal interface IApplicationLauncher
{
    Task<ApplicationLaunchResult> RunConnectAsync(string executable, string configurationPath,
        IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken,
        Action<int>? processStarted = null, Func<Task>? processReleasing = null);

    /// <summary>Waits for advertised readiness; legacy payloads retain process-only handoff.</summary>
    Task<ApplicationLaunchResult> StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken, Action<int>? processStarted = null, Func<Task>? processReleasing = null);
}

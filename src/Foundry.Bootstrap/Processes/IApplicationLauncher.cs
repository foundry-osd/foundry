// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Processes;

/// <summary>Launches each application once; cancelling observation never terminates the child.</summary>
internal interface IApplicationLauncher
{
    Task<int> RunConnectAsync(string executable, string configurationPath,
        IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken);

    /// <summary>Returns the process ID after launch, without claiming application readiness.</summary>
    Task<int> StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken);
}

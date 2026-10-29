// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Security;

/// <summary>
/// Receives the deployment key through the pipe named by <see cref="DeploymentKeyHandoff.ReceivePipeEnvironmentVariable"/>.
/// </summary>
/// <remarks>
/// The variable is read once at construction and removed from the process environment right away (L4-19), so no child
/// started by a deployment step inherits the pipe name; the receiver is constructed with <see cref="DeploymentAccessGate"/>
/// when the main window view model is resolved, before any deployment step runs. The pipe name is never logged.
/// <see cref="DeploymentKeyHandoff.TryReceiveAsync"/> is bounded by <see cref="DeploymentKeyHandoff.ClientTimeout"/> and
/// returns <see langword="null"/> on every pipe failure, so a missing server never stalls startup. A valid name that yields
/// no key is logged once at Debug, without the name.
/// </remarks>
public sealed class DeploymentKeyHandoffReceiver(ILogger<DeploymentKeyHandoffReceiver> logger) : IDeploymentKeyHandoffReceiver
{
    private string? _pipeName = ReadAndRemovePipeName();

    /// <inheritdoc />
    public async Task<byte[]?> TryReceiveAsync(CancellationToken cancellationToken)
    {
        // Taking the name consumes the hand-off, so a second call never reaches a pipe.
        string? pipeName = Interlocked.Exchange(ref _pipeName, null);
        if (!DeploymentKeyHandoff.IsValidPipeName(pipeName))
        {
            logger.LogDebug("No deployment key hand-off is available.");
            return null;
        }

        byte[]? key = await DeploymentKeyHandoff.TryReceiveAsync(pipeName, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            logger.LogDebug("The deployment key hand-off was not received.");
        }

        return key;
    }

    private static string? ReadAndRemovePipeName()
    {
        string? pipeName = Environment.GetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable);
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable, null);
        return pipeName;
    }
}

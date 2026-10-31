// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Security;

/// <summary>Hands the unlocked deployment key to Foundry Bootstrap, which serves it to Foundry Deploy.</summary>
public interface IDeploymentKeyHandoffSender
{
    /// <summary>Writes the deployment key to the Bootstrap hand-off pipe, at most once per process.</summary>
    /// <param name="deploymentKey">The 32-byte deployment key; the caller keeps ownership and zeroes it.</param>
    /// <param name="cancellationToken">
    /// Bounds the send; its cancellation is the only failure that propagates. The unlock service passes its own bounded timeout
    /// token, not linked to its caller, because the secrets are already unlocked when the key is sent.
    /// </param>
    /// <returns><see langword="true"/> when the key was written; <see langword="false"/> when no hand-off is available, it was already used, or the pipe failed.</returns>
    Task<bool> TrySendAsync(byte[] deploymentKey, CancellationToken cancellationToken);
}

/// <summary>
/// Sends the deployment key through the pipe named by <see cref="DeploymentKeyHandoff.SendPipeEnvironmentVariable"/>.
/// </summary>
/// <remarks>
/// The variable is read once at construction and removed from the process environment right away, so no child
/// started by Connect (for example <c>netsh</c>) inherits the pipe name; the composition root constructs this
/// sender with the main window graph, before provisioning starts. The pipe name is never logged.
/// </remarks>
public sealed class EnvironmentDeploymentKeyHandoffSender(ILogger<EnvironmentDeploymentKeyHandoffSender> logger) : IDeploymentKeyHandoffSender
{
    private string? _pipeName = ReadAndRemovePipeName();

    /// <inheritdoc />
    public Task<bool> TrySendAsync(byte[] deploymentKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deploymentKey);

        // Taking the name consumes the hand-off, so a second call never reaches a pipe.
        string? pipeName = Interlocked.Exchange(ref _pipeName, null);
        if (!DeploymentKeyHandoff.IsValidPipeName(pipeName))
        {
            logger.LogDebug("No deployment key hand-off is available.");
            return Task.FromResult(false);
        }

        return DeploymentKeyHandoff.TrySendAsync(pipeName, deploymentKey, cancellationToken);
    }

    private static string? ReadAndRemovePipeName()
    {
        string? pipeName = Environment.GetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable);
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable, null);
        return pipeName;
    }
}

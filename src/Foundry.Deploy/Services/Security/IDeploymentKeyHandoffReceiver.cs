// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Security;

/// <summary>
/// Receives the deployment key Foundry Bootstrap relays from Foundry Connect after a successful unlock (L4-1, L4-2).
/// </summary>
/// <remarks>
/// A received key is not trusted: <see cref="DeploymentAccessGate"/> accepts it only when the media key check
/// authenticates it, and prompts for the deployment password otherwise.
/// </remarks>
public interface IDeploymentKeyHandoffReceiver
{
    /// <summary>Reads the handed-off deployment key, at most once per process.</summary>
    /// <param name="cancellationToken">Caller cancellation, the only failure that propagates.</param>
    /// <returns>
    /// A 32-byte key the caller owns and must zero with <c>CryptographicOperations.ZeroMemory</c>,
    /// or <see langword="null"/> when no hand-off is available, it was already used, or the pipe failed.
    /// </returns>
    Task<byte[]?> TryReceiveAsync(CancellationToken cancellationToken);
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Refreshes what depends on the network after the reopened Foundry Connect page returned to Deploy. Called on the UI thread,
/// after Deploy's content is shown again with the returned theme and language.
/// </summary>
public interface INetworkReturnRefresh
{
    /// <summary>Refreshes the network-dependent state.</summary>
    /// <param name="cancellationToken">Cancelled when the phase ends.</param>
    Task RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>The refresh that refreshes nothing.</summary>
internal sealed class NoNetworkReturnRefresh : INetworkReturnRefresh
{
    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

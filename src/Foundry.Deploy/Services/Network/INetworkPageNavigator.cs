// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Reopens the Foundry Connect page from Foundry Deploy while Deploy stays alive, then shows Deploy's content again. One
/// instance per Deploy phase; used on the UI thread.
/// </summary>
public interface INetworkPageNavigator
{
    /// <summary>Gets whether the host can show the Connect page (it supplied <see cref="DeployPhaseOptions.OpenNetworkPage"/>).</summary>
    bool IsAvailable { get; }

    /// <summary>Gets whether the Connect page is shown, from the request until Deploy's content is shown again.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Shows the Connect page and, when it closes, Deploy's content with the theme and language chosen there, then refreshes
    /// what depends on the network (<see cref="INetworkReturnRefresh"/>).
    /// </summary>
    /// <param name="cancellationToken">Closes the page; the end of the phase closes it too.</param>
    /// <returns>
    /// <see langword="true"/> when the page was shown and closed normally; <see langword="false"/> when the host cannot show it,
    /// it is already open, the phase ended, or the page or the refresh failed or was cancelled. These cases never throw.
    /// </returns>
    Task<bool> OpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes a page that is still open, refuses later requests, and completes once the reopen in flight has finished, so the
    /// page's own log scope ends before Deploy's. Call it on the UI thread when the phase ends: the host's token callbacks run
    /// on the calling thread. Never throws.
    /// </summary>
    Task CancelAndWaitAsync();
}

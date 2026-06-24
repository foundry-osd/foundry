// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Captures eligible Foundry-managed network profiles for deployed Windows import.
/// </summary>
public interface INetworkProfileRoamingService
{
    /// <summary>
    /// Captures a Wi-Fi profile into the runtime roaming artifact.
    /// </summary>
    /// <param name="request">The capture request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the capture operation.</returns>
    Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Captures a wired 802.1X profile into the runtime roaming artifact.
    /// </summary>
    /// <param name="request">The capture request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the capture operation.</returns>
    Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken);
}

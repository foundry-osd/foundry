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
    /// Captures ordered Wi-Fi profiles into the runtime roaming artifact, replacing the captured Wi-Fi profiles. The
    /// first written profile is the preferred one; a request whose source is unavailable is skipped with a Warning and
    /// the next one takes its place.
    /// </summary>
    /// <param name="requests">The capture requests, most preferred first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the capture operation.</returns>
    Task CaptureWifiProfilesAsync(IReadOnlyList<NetworkProfileRoamingCaptureRequest> requests, CancellationToken cancellationToken);

    /// <summary>
    /// Captures a wired 802.1X profile into the runtime roaming artifact.
    /// </summary>
    /// <param name="request">The capture request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the capture operation.</returns>
    Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken);
}

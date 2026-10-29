// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Network;

/// <summary>
/// Result of the once-per-process network provisioning.
/// </summary>
/// <param name="Wired">Wired 802.1X state after provisioning.</param>
/// <param name="Wifi">Provisioned Wi-Fi profile state.</param>
/// <param name="WifiProfile">
/// The provisioned profile document when <paramref name="Wifi"/> is <see cref="ProvisionedWifiStatus.Ready"/>. It is
/// kept until exit so the profile can be set again on a Wi-Fi adapter that appears later (L2-6).
/// </param>
/// <param name="Failures">Failures met while provisioning, in order; the coordinator logs and reports them.</param>
public sealed record ProvisioningOutcome(Dot1xState Wired, ProvisionedWifiState Wifi, WifiProfileDocument? WifiProfile, IReadOnlyList<NetworkFailureCode> Failures)
{
    /// <summary>
    /// Gets the outcome of a media without wired or Wi-Fi provisioning.
    /// </summary>
    public static ProvisioningOutcome NotConfigured { get; } = new(Dot1xState.NotConfigured, ProvisionedWifiState.NotConfigured, null, []);
}

/// <summary>
/// Applies the media network provisioning: certificates, the wired 802.1X profile and the provisioned Wi-Fi profile.
/// </summary>
public interface IProvisioningStep
{
    /// <summary>
    /// Runs the provisioning. The first call does the work; every later call returns the same outcome instance. A
    /// cancelled run is not kept, so the next call starts again.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The provisioning outcome; failures are values, never exceptions.</returns>
    Task<ProvisioningOutcome> RunAsync(CancellationToken cancellationToken);
}

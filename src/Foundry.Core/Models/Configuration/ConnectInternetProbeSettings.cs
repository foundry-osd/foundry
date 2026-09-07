// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines the HTTP probes Foundry.Connect uses to validate Internet reachability.
/// </summary>
public sealed record ConnectInternetProbeSettings
{
    /// <summary>Gets the ordered exact response expectations used for connectivity checks.</summary>
    public IReadOnlyList<ConnectInternetProbeEndpoint> Probes { get; init; } = [ConnectInternetProbeEndpoint.Microsoft];
    /// <summary>
    /// Gets the per-probe timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5;
}

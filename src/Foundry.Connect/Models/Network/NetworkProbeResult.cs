// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Models.Network;

public enum NetworkReadinessStatus
{
    Online, NoLink, NoUsableAddress, NameResolutionFailed, ProxyAuthenticationRequired,
    UnexpectedResponse, TimedOut, Unavailable
}

public sealed record NetworkProbeResult(NetworkReadinessStatus Status, string? ErrorCode);

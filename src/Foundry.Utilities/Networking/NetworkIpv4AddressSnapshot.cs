// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Networking;

/// <summary>
/// Describes an IPv4 address and its subnet mask.
/// </summary>
public sealed record NetworkIpv4AddressSnapshot(string Address, string? SubnetMask);

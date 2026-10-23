// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Identifies how a network cache scope value is written.</summary>
public enum NetworkCacheScopeKind
{
    /// <summary>A CIDR subnet such as <c>10.10.0.0/16</c> or <c>fd00:10::/48</c>.</summary>
    Subnet,
    /// <summary>An inclusive range such as <c>10.20.0.10-10.20.0.200</c>.</summary>
    Range,
    /// <summary>One address.</summary>
    Address
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines the local account role provisioned during Windows OOBE.
/// </summary>
public enum OobeAccountType
{
    /// <summary>
    /// Creates a standard local user account.
    /// </summary>
    Standard,

    /// <summary>
    /// Creates a local user account in the Administrators group.
    /// </summary>
    Administrator
}

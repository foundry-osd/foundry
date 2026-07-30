// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

/// <summary>
/// Describes the Secure Boot capability and state reported by the current firmware.
/// </summary>
public enum SecureBootState
{
    Unknown,
    Unsupported,
    Disabled,
    Enabled
}

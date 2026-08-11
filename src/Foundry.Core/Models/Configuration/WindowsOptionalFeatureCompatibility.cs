// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the known compatibility of an optional feature with selected Windows targets.
/// </summary>
public enum WindowsOptionalFeatureCompatibility
{
    Available,
    PartiallyAvailable,
    Unavailable,
    RuntimeVerificationRequired
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Classifies whether boot media should be rebuilt for a runtime release.</summary>
public enum BootMediaUpdateReason
{
    None,
    NewerRelease,
    UnknownAuthoringVersion
}

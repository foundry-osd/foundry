// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>Separates positive servicing evidence from missing evidence and an unreadable installation.</summary>
public enum AdkServicingState
{
    Unknown,
    NotVerified,
    Verified
}

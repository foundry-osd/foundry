// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeLanguageDiscoveryOptions
{
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeToolPaths? Tools { get; init; }
}

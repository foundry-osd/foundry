// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDismProgress
{
    public int Percent { get; init; }

    public string Status { get; init; } = string.Empty;
}

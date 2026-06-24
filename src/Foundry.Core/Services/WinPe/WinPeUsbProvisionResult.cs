// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeUsbProvisionResult
{
    public string BootDriveLetter { get; init; } = string.Empty;
    public string CacheDriveLetter { get; init; } = string.Empty;
}

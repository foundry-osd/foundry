// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

public sealed record MediaOperationResult(Guid OperationId, MediaOperationTarget Target,
    string? IsoOutputPath = null, WinPeUsbProvisionResult? UsbResult = null);

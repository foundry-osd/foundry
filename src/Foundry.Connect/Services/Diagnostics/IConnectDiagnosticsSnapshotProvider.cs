// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Diagnostics;

namespace Foundry.Connect.Services.Diagnostics;

public interface IConnectDiagnosticsSnapshotProvider
{
    Task<ConnectDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

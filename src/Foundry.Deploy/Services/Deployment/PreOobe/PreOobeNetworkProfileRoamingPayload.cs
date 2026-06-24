// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Carries generated network profile roaming data files for the pre-OOBE importer.
/// </summary>
public sealed record PreOobeNetworkProfileRoamingPayload
{
    /// <summary>
    /// Gets generated data files staged under the pre-OOBE data folder.
    /// </summary>
    public IReadOnlyList<PreOobeScriptDataFile> DataFiles { get; init; } = [];
}

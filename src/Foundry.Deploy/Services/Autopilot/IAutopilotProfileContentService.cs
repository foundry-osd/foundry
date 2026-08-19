// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Autopilot;

public interface IAutopilotProfileContentService
{
    Task<byte[]> ReadAsync(AutopilotProfileCatalogItem profile, CancellationToken cancellationToken = default);
}

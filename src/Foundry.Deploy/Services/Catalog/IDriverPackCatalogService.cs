// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public interface IDriverPackCatalogService
{
    Task<IReadOnlyList<DriverPackCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default);
}

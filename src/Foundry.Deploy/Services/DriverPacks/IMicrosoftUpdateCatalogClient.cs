// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MicrosoftUpdateCatalogUpdate>> SearchAsync(
        string searchQuery,
        bool descending = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MicrosoftUpdateCatalogDownload>> GetDownloadsAsync(
        string updateId,
        CancellationToken cancellationToken = default);
}

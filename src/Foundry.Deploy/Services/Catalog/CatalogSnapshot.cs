// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Catalog;

public sealed record CatalogSnapshot<T>(IReadOnlyList<T> Items, string Revision, Uri SourceUri,
    DateTimeOffset RetrievedUtc, bool IsOffline);

public sealed record CatalogLoadRequest(bool OfflineOnly, bool RequireDriverPacks);

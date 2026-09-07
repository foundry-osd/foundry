// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Catalog;

public sealed record VerifiedCatalogDocument(string Id, byte[] Content, string Sha256,
    string Revision, Uri SourceUri, DateTimeOffset RetrievedUtc);

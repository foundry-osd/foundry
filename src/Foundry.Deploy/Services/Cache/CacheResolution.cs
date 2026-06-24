// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Cache;

public sealed record CacheResolution
{
    public required string RootPath { get; init; }
    public required string Source { get; init; }
}

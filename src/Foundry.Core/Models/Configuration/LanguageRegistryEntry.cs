// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

public sealed record LanguageRegistryEntry
{
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

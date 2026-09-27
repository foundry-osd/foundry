// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Stores ordered authoring choices without embedding package content or local source paths.</summary>
public sealed record PreOobeSettings
{
    public bool IsEnabled { get; init; }
    public IReadOnlyList<PreOobeActionSettings> Actions { get; init; } = [];
}

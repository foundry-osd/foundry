// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Localization;

/// <summary>
/// Describes one UI culture that can be selected by an application.
/// </summary>
public sealed record SupportedCultureDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SupportedCultureDefinition" /> class.
    /// </summary>
    /// <param name="code">Culture code accepted by <see cref="CultureInfo" />.</param>
    /// <param name="resourceKey">Resource key used to resolve the localized display name.</param>
    /// <param name="sortOrder">Display order for language selection UI.</param>
    public SupportedCultureDefinition(string code, string resourceKey, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        Code = CultureInfo.GetCultureInfo(code.Trim().Replace('_', '-')).Name;
        ResourceKey = resourceKey;
        SortOrder = sortOrder;
    }

    /// <summary>
    /// Gets the canonical culture code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the resource key used for the language display name.
    /// </summary>
    public string ResourceKey { get; }

    /// <summary>
    /// Gets the display sort order.
    /// </summary>
    public int SortOrder { get; }
}

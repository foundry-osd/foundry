// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Answers whether a resource key is defined in the Connect resources.
/// </summary>
internal static class ConnectResources
{
    private static readonly ResourceManager Manager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

    /// <summary>
    /// Checks the neutral (en-US) resource set only, so a key missing from en-US is reported even when a satellite
    /// culture defines it.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns><see langword="true"/> when the neutral set holds a non-empty value for the key.</returns>
    public static bool Exists(string key) =>
        !string.IsNullOrEmpty(Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)?.GetString(key));
}

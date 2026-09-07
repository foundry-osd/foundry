// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Catalog;

public static class VerifiedCatalogSources
{
    public const string OperatingSystems = "operating-systems";
    public const string DriverPacks = "driver-packs";
    public static Uri GetUri(string id) => id switch
    {
        OperatingSystems => new("https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/OS/OperatingSystem.xml"),
        DriverPacks => new("https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/DriverPack/DriverPack_Unified.xml"),
        _ => throw new ArgumentException("Unknown catalog identity.", nameof(id))
    };
    public static string GetRelativePath(string id)
    {
        _ = GetUri(id);
        return $"Foundry/Catalogs/{id}.xml";
    }
}

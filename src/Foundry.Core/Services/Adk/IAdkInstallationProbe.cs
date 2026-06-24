// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

public interface IAdkInstallationProbe
{
    string? GetKitsRootPath();
    bool DirectoryExists(string path);
    bool FileExists(string path);
    bool DirectoryContainsFile(string directoryPath, string fileName);
    IReadOnlyList<AdkInstalledProduct> GetInstalledProducts();
}

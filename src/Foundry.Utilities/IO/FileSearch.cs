// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Provides recursive file searches.
/// </summary>
public static class FileSearch
{
    /// <summary>
    /// Determines whether a file matching a search pattern exists under a directory.
    /// </summary>
    /// <param name="rootPath">The root directory to search.</param>
    /// <param name="searchPattern">The file search pattern.</param>
    /// <returns><see langword="true"/> when a matching file exists; otherwise, <see langword="false"/>.</returns>
    public static bool ContainsRecursive(string rootPath, string searchPattern)
    {
        return Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories).Any();
    }
}

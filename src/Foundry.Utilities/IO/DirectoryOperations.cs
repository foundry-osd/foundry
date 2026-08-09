// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Provides directory lifecycle operations.
/// </summary>
public static class DirectoryOperations
{
    /// <summary>
    /// Recursively deletes an existing directory and all of its contents, then recreates the directory.
    /// </summary>
    /// <param name="path">The directory to recreate.</param>
    public static void Recreate(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}

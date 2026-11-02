// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Files;

/// <summary>Detects reparse points on a path and its existing ancestors without following them.</summary>
internal static class RedirectedPathDetector
{
    /// <summary>Returns whether <paramref name="path"/> or any existing ancestor is a reparse point.</summary>
    /// <remarks>
    /// Staging validates every destination before it exists; skipping missing entries avoids one exception per
    /// missing folder, which turns large driver sets into thousands of exceptions. The catches cover a race only.
    /// </remarks>
    internal static bool ContainsReparsePoint(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (!Path.Exists(current)) continue;
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }
}

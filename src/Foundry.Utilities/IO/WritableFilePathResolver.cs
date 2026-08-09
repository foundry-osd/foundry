// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Resolves a file path under the first candidate directory that can be created.
/// </summary>
public static class WritableFilePathResolver
{
    /// <summary>
    /// Returns a path under the first creatable directory, or the file name when every candidate fails.
    /// </summary>
    public static string Resolve(IEnumerable<string> candidateDirectories, string fileName)
    {
        ArgumentNullException.ThrowIfNull(candidateDirectories);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        foreach (string candidateDirectory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidateDirectory);
                return Path.Combine(candidateDirectory, fileName);
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return fileName;
    }
}

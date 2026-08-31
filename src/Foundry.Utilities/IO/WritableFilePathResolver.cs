// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Resolves a file path under the first candidate directory where the target file can be opened for writing.
/// </summary>
public static class WritableFilePathResolver
{
    /// <summary>
    /// Returns an absolute path under the first writable directory, or an absolute current-directory fallback.
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
                string candidatePath = Path.Combine(candidateDirectory, fileName);
                using (new FileStream(
                    candidatePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                }

                return Path.GetFullPath(candidatePath);
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return Path.GetFullPath(fileName);
    }
}

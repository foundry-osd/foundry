// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

/// <summary>
/// Reports that a downloaded artifact does not match its expected hash.
/// Derives from <see cref="InvalidOperationException"/> so callers that already handle that type keep working,
/// while callers that map failures to user-facing messages can tell a corrupted download from invalid catalog metadata.
/// </summary>
public sealed class ArtifactHashMismatchException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactHashMismatchException"/> class.
    /// </summary>
    /// <param name="message">Detailed local diagnostic message.</param>
    public ArtifactHashMismatchException(string message)
        : base(message)
    {
    }
}

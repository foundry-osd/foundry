// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Drivers;

/// <summary>Copies a verified custom driver set from the boot media to the target, ready for offline INF injection.</summary>
public interface ICustomDriverSetStagingService
{
    /// <summary>
    /// Copies every file of <paramref name="set"/> to <c>&lt;destinationRoot&gt;\&lt;ContentHash&gt;</c>, checking each file's length
    /// and SHA-256 against the verified package manifest while it is copied.
    /// </summary>
    /// <param name="set">The selected set, with its verified manifest and media root.</param>
    /// <param name="destinationRoot">Folder that receives the content-hash folder; it must be on the target disk.</param>
    /// <param name="progress">Optional copy progress, in percent of the set length.</param>
    /// <param name="cancellationToken">Token that stops the copy.</param>
    /// <returns>The staged content-hash folder.</returns>
    /// <exception cref="InvalidDataException">The set identity or a file failed verification.</exception>
    /// <exception cref="IOException">The media or a file is missing, or the copy failed.</exception>
    /// <remarks>Fails closed: on any failure, including cancellation, the content-hash folder is removed so no partial set remains.</remarks>
    Task<string> StageAsync(CustomDriverSetOption set, string destinationRoot, IProgress<double>? progress, CancellationToken cancellationToken);
}

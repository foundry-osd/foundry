// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Reads structured metadata for every image in a Windows image container.
/// </summary>
public interface IWindowsImageInfoReader
{
    /// <summary>
    /// Returns detached metadata after releasing the native image information buffer.
    /// </summary>
    Task<IReadOnlyList<WindowsImageInfo>> ReadAsync(string imagePath, CancellationToken cancellationToken = default);
}

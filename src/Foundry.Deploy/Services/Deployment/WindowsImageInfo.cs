// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Describes an image using native DISM metadata, including its exact expanded byte count.
/// </summary>
/// <param name="Index">The one-based image index.</param>
/// <param name="Name">The image name stored in the container.</param>
/// <param name="EditionId">The Windows edition identifier.</param>
/// <param name="SizeBytes">The expanded image size without signed or 32-bit narrowing.</param>
/// <param name="Architecture">The normalized processor architecture: x86, x64, arm64, or unknown.</param>
/// <param name="Version">The image major, minor, and build version.</param>
public sealed record WindowsImageInfo(int Index, string Name, string EditionId, ulong SizeBytes, string Architecture, Version Version);

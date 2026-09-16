// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Captures the unique selected edition and DISM-declared expanded bytes. The size is a
/// capacity estimate; it does not include later servicing, drivers, or file-system overhead.
/// </summary>
/// <param name="Index">The unique image index matching the requested edition ID.</param>
/// <param name="EditionId">The exact DISM edition ID.</param>
/// <param name="SizeBytes">Positive declared expanded size of the selected image.</param>
/// <param name="SetupMediaSizeBytes">Declared setup-media expansion, when available.</param>
public sealed record WindowsImageMetadata(int Index, string EditionId, long SizeBytes, long? SetupMediaSizeBytes = null);

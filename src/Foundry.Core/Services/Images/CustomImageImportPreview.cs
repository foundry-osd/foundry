// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

/// <summary>Describes a source before copying; import reopens and validates the source independently.</summary>
public sealed record CustomImageImportPreview(long Length, IReadOnlyList<CustomImageIndex> Indexes, bool HasOptionalFeatureSources);

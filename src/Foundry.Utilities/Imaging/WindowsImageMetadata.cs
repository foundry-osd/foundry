// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Imaging;

/// <summary>Copies native image metadata without retaining unmanaged pointers or inferring product support.</summary>
public sealed record WindowsImageMetadata(int Index, string Name, string EditionId, ulong ImageSize,
    string Architecture, Version Version, string Description, string ProductType,
    IReadOnlyList<string> Languages, int DefaultLanguageIndex);

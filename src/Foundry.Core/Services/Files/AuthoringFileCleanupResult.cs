// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Files;

/// <summary>The outcome of <see cref="AuthoringFileLibrary.DeleteUnreferenced"/>.</summary>
/// <param name="Removed">Files deleted because nothing references or holds them.</param>
/// <param name="Kept">Files kept because they are referenced, leased or imported after the references were read.</param>
/// <param name="Failed">Unreferenced files that could not be deleted; a later cleanup retries them.</param>
public sealed record AuthoringFileCleanupResult(int Removed, int Kept, int Failed);

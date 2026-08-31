// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Reports the published archive path, included archive entry names, and omitted source file names.
/// </summary>
public sealed record SupportBundleResult(
    string ArchivePath,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> OmittedFiles);

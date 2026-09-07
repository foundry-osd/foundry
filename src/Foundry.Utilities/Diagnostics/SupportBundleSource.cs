// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Diagnostics;

/// <summary>Identifies an explicitly selected diagnostic source and its fixed archive name.</summary>
public sealed record SupportBundleSource(string Path, string ArchiveName, SupportBundleSourceFormat Format, bool Required);

/// <summary>Lists the reviewed diagnostic formats supported by the exporter.</summary>
public enum SupportBundleSourceFormat
{
    ApplicationText,
    NativeText,
    ActionResultJson
}

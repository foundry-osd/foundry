// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Describes the diagnostic files and safe runtime summary to include in a support archive.
/// </summary>
public sealed record SupportBundleRequest
{
    public required string ApplicationName { get; init; }
    public required string ApplicationVersion { get; init; }
    public required string SessionId { get; init; }
    public required string DestinationDirectoryPath { get; init; }
    public required IReadOnlyCollection<string> LogFilePaths { get; init; }
    public IReadOnlyDictionary<string, string> Summary { get; init; } = new Dictionary<string, string>();
    public SupportBundlePrivacyMode PrivacyMode { get; init; } = SupportBundlePrivacyMode.Sanitized;
}

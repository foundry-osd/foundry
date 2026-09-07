// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

internal static class ApplicationUpdateSourceClassifier
{
    public static bool IsGitHubRepositoryUrl(string feedUrl) =>
        Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2;

    public static bool IsPrereleaseChannel(string? channel) => channel?.Trim().ToLowerInvariant() is "beta" or "preview" or "prerelease";
}

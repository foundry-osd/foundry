// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Validates the network cache section for authoring readiness and media generation.</summary>
public static class NetworkCacheConfigurationValidator
{
    /// <summary>
    /// Returns every issue of an enabled section. A disabled section is always ready. Only accounts used by a share
    /// need a password; any share with an account requires Deploy password protection, because a PXE boot image can be
    /// downloaded by anyone on the network.
    /// </summary>
    public static NetworkCacheValidationResult EvaluateReadiness(NetworkCacheSettings settings, Func<string, bool> hasPassword, bool isMediaProtected)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hasPassword);
        if (!settings.IsEnabled) return new(true, []);
        var issues = new List<NetworkCacheValidationIssue>();
        if (settings.Shares.Count == 0) issues.Add(new(NetworkCacheValidationCode.SharesRequired));
        if (settings.Scopes.Count == 0) issues.Add(new(NetworkCacheValidationCode.ScopesRequired));
        var accountIds = new HashSet<string>(settings.Accounts.Select(account => account.Id), StringComparer.Ordinal);
        var shareIds = new HashSet<string>(settings.Shares.Select(share => share.Id), StringComparer.Ordinal);

        foreach (NetworkCacheAccount account in settings.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.UserName)) issues.Add(new(NetworkCacheValidationCode.UserNameRequired, account.Id));
        }

        foreach (NetworkCacheShare share in settings.Shares)
        {
            if (!IsValidSharePath(share.Path)) issues.Add(new(NetworkCacheValidationCode.InvalidSharePath, share.Id));
            if (share.AccountId is null) continue;
            if (!accountIds.Contains(share.AccountId)) issues.Add(new(NetworkCacheValidationCode.UnknownAccount, share.Id));
        }

        foreach (string accountId in UsedAccountIds(settings).Where(accountIds.Contains))
        {
            if (!hasPassword(accountId)) issues.Add(new(NetworkCacheValidationCode.PasswordRequired, accountId));
        }

        foreach (NetworkCacheScope scope in settings.Scopes)
        {
            if (!NetworkCacheScopeParser.TryParse(scope.Kind, scope.Value, out _)) issues.Add(new(NetworkCacheValidationCode.InvalidScope, scope.Id));
            if (scope.ShareIds.Count == 0) issues.Add(new(NetworkCacheValidationCode.ScopeWithoutShare, scope.Id));
            else if (scope.ShareIds.Any(id => !shareIds.Contains(id))) issues.Add(new(NetworkCacheValidationCode.UnknownShare, scope.Id));
        }

        if (RequiresMediaProtection(settings) && !isMediaProtected) issues.Add(new(NetworkCacheValidationCode.MediaProtectionRequired));
        return new(issues.Count == 0, issues);
    }

    /// <summary>Tells whether the enabled section embeds share credentials in the media, which requires Deploy password protection.</summary>
    public static bool RequiresMediaProtection(NetworkCacheSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsEnabled && settings.Shares.Any(share => !string.IsNullOrWhiteSpace(share.AccountId));
    }

    /// <summary>Returns the ids of accounts referenced by at least one share.</summary>
    public static IReadOnlyCollection<string> UsedAccountIds(NetworkCacheSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Shares
            .Select(share => share.AccountId)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Accepts <c>\\server\share</c> with optional sub-folders; rejects drive paths, relative segments and a bare server.</summary>
    public static bool IsValidSharePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = path[2..].TrimEnd('\\').Split('\\');
        return segments.Length >= 2 &&
               segments.All(segment => segment.Length > 0 && segment is not "." and not ".." &&
                                       segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}

/// <summary>Readiness of the network cache section.</summary>
public sealed record NetworkCacheValidationResult(bool IsValid, IReadOnlyList<NetworkCacheValidationIssue> Issues);

/// <summary>One readiness issue and the account, share or scope it concerns when there is one.</summary>
public sealed record NetworkCacheValidationIssue(NetworkCacheValidationCode Code, string? ItemId = null);

/// <summary>Stable codes mapped to localized text by the page.</summary>
public enum NetworkCacheValidationCode
{
    SharesRequired,
    ScopesRequired,
    InvalidSharePath,
    InvalidScope,
    ScopeWithoutShare,
    UnknownShare,
    UnknownAccount,
    UserNameRequired,
    PasswordRequired,
    MediaProtectionRequired
}

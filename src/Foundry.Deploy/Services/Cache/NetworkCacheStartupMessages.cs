// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Services.Cache;

/// <summary>
/// Turns a network cache startup failure into the blocking message shown before the wizard: one sentence of cause naming the
/// share, account or device address when known, then where to fix it (Foundry OSD Network cache page or the server).
/// </summary>
public static class NetworkCacheStartupMessages
{
    /// <summary>Number of missing items named in the message before an ellipsis.</summary>
    public const int MaximumListedItems = 5;

    /// <summary>Returns the localized title, the message and whether a retry can help.</summary>
    /// <param name="failure">Why the network cache prevents the deployment from starting.</param>
    /// <param name="localization">Resolves the Deploy strings in the active UI culture.</param>
    public static (string Title, string Message, bool CanRetry) Format(NetworkCacheStartupFailure failure, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(localization);

        string message = failure.Kind switch
        {
            NetworkCacheStartupFailureKind.NoScope => FormatOrGeneric(localization, "NetworkCacheStartup.NoScope", failure.DeviceAddress),
            NetworkCacheStartupFailureKind.Unreachable => FormatOrGeneric(localization, "NetworkCacheStartup.Unreachable", failure.SharePath),
            NetworkCacheStartupFailureKind.AccessDenied => FormatAccessDenied(localization, failure),
            NetworkCacheStartupFailureKind.NotPublished or NetworkCacheStartupFailureKind.ContentMissing =>
                FormatOrGeneric(localization, "NetworkCacheStartup.NotPublished", failure.SharePath),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null)
        };

        if (failure.Kind == NetworkCacheStartupFailureKind.ContentMissing && failure.MissingItems.Count > 0)
        {
            message += Environment.NewLine + Environment.NewLine +
                       localization.Format("NetworkCacheStartup.MissingItemsFormat", ListItems(localization, failure.MissingItems));
        }

        return (localization.GetString("NetworkCacheStartup.Title"), message, failure.CanRetry);
    }

    private static string FormatAccessDenied(ILocalizationService localization, NetworkCacheStartupFailure failure)
    {
        if (string.IsNullOrWhiteSpace(failure.SharePath))
        {
            return localization.GetString("NetworkCacheStartup.AccessDenied");
        }

        return string.IsNullOrWhiteSpace(failure.AccountName)
            ? localization.Format("NetworkCacheStartup.AccessDeniedShareFormat", failure.SharePath)
            : localization.Format("NetworkCacheStartup.AccessDeniedFormat", failure.AccountName, failure.SharePath);
    }

    // Each cause has a "<key>Format" text naming the value and a "<key>" text for when the value is unknown.
    private static string FormatOrGeneric(ILocalizationService localization, string key, string? value) =>
        string.IsNullOrWhiteSpace(value) ? localization.GetString(key) : localization.Format(key + "Format", value);

    private static string ListItems(ILocalizationService localization, IReadOnlyList<string> items)
    {
        IEnumerable<string> listed = items.Take(MaximumListedItems).Select(item => item switch
        {
            // The presence check reports these English labels when the index names no item; show the localized ones.
            NetworkCacheContentPresence.CustomImagesItem => localization.GetString("NetworkCacheStartup.CustomImagesItem"),
            NetworkCacheContentPresence.CustomDriverSetsItem => localization.GetString("NetworkCacheStartup.CustomDriverSetsItem"),
            _ => item
        });
        string list = string.Join(", ", listed);
        return items.Count > MaximumListedItems ? list + ", …" : list;
    }
}

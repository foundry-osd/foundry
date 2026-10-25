// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>Maps the typed sharing statuses to their resource keys and severities; no status is a string.</summary>
public static class SharingStatusText
{
    /// <summary>Returns the resource key of <paramref name="status"/> as <paramref name="role"/> reports it.</summary>
    public static string Key(SharingRole role, SharingStatus status) => status switch
    {
        SharingStatus.Idle => role switch
        {
            SharingRole.Reference => "Sharing.StatusIdleReference",
            SharingRole.Follower => "Sharing.StatusIdleFollower",
            _ => "Sharing.StatusIdle"
        },
        SharingStatus.Publishing => "Sharing.StatusPublishing",
        SharingStatus.Published => "Sharing.StatusPublished",
        SharingStatus.NotPublishedIncomplete => "Sharing.StatusNotPublishedIncomplete",
        SharingStatus.Checking => "Sharing.StatusChecking",
        SharingStatus.UpToDate => "Sharing.StatusUpToDate",
        SharingStatus.Updated => "Sharing.StatusUpdated",
        SharingStatus.ContentDownloading => "Sharing.StatusContentDownloading",
        SharingStatus.FolderUnavailable => "Sharing.StatusFolderUnavailable",
        SharingStatus.FolderNotEmpty => "Sharing.StatusFolderNotEmpty",
        SharingStatus.WrongPassword => role == SharingRole.Follower ? "Sharing.StatusWrongPassword" : "Sharing.StatusWrongPasswordReference",
        SharingStatus.Tampered => "Sharing.StatusTampered",
        SharingStatus.UnknownPublisher => role == SharingRole.Follower ? "Sharing.StatusUnknownPublisher" : "Sharing.StatusUnknownPublisherReference",
        SharingStatus.RollbackRejected => "Sharing.StatusRollbackRejected",
        SharingStatus.Unsupported => "Sharing.StatusUnsupported",
        SharingStatus.PasswordsNeeded => role == SharingRole.Follower ? "Sharing.StatusPasswordsNeeded" : "Sharing.StatusPasswordsNeededReference",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown sharing status.")
    };

    /// <summary>Returns how prominently <paramref name="status"/> is shown.</summary>
    public static InfoBarSeverity Severity(SharingStatus status) => status switch
    {
        SharingStatus.Published or SharingStatus.UpToDate or SharingStatus.Updated => InfoBarSeverity.Success,
        SharingStatus.Idle or SharingStatus.Publishing or SharingStatus.Checking or SharingStatus.ContentDownloading => InfoBarSeverity.Informational,
        SharingStatus.NotPublishedIncomplete or SharingStatus.PasswordsNeeded or SharingStatus.RollbackRejected
            or SharingStatus.UnknownPublisher => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Error
    };
}

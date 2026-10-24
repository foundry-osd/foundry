// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>What a PC does with configuration sharing (spec 9).</summary>
public enum SharingRole
{
    /// <summary>A normal PC: it neither publishes nor follows.</summary>
    None,

    /// <summary>The PC publishes its configuration to a folder.</summary>
    Reference,

    /// <summary>The PC follows the configuration published in a folder; published sections are read-only.</summary>
    Follower
}

/// <summary>
/// State reported by <see cref="ConfigurationSharingEngine"/> (spec 9). The user interface maps each value to a resource key; no
/// status is a string.
/// </summary>
public enum SharingStatus
{
    /// <summary>Nothing to report: no role, or no publish or check ran yet.</summary>
    Idle,

    /// <summary>Reference: a publish is running (content is encrypted and copied, then the manifest is activated).</summary>
    Publishing,

    /// <summary>Reference: the configuration is published; also the outcome of a password change.</summary>
    Published,

    /// <summary>
    /// Reference: the configuration is not ready to build or references content missing locally; the previous version stays
    /// current and the next change or Retry publishes again.
    /// </summary>
    NotPublishedIncomplete,

    /// <summary>
    /// Follower: the folder is not consistent yet (a cloud folder delivered <c>current.json</c> before its manifest, or a manifest
    /// read as modified once); the next check retries.
    /// </summary>
    Checking,

    /// <summary>Follower: the applied configuration is the current publication and its content is in the local libraries.</summary>
    UpToDate,

    /// <summary>Follower: a newer publication was applied and its content is in the local libraries ("Configuration updated").</summary>
    Updated,

    /// <summary>
    /// Follower: the configuration is applied and its content is still being fetched, or could not all be fetched yet (see
    /// <see cref="ConfigurationSharingEngine.FailedContent"/> and <see cref="ConfigurationSharingEngine.IsContentStalled"/>). Media
    /// readiness stays false until the content is in the libraries.
    /// </summary>
    ContentDownloading,

    /// <summary>The folder is missing or cannot be read or written, or (follower) holds no publication any more.</summary>
    FolderUnavailable,

    /// <summary>
    /// Reference: the folder holds files that are not a publication this PC can continue, or the publication cannot take another
    /// version; choose an empty folder.
    /// </summary>
    FolderNotEmpty,

    /// <summary>The publication password does not open the current manifest.</summary>
    WrongPassword,

    /// <summary>Follower: the current manifest was modified; reported only after two consecutive reads of the same manifest.</summary>
    Tampered,

    /// <summary>
    /// The folder is signed by another key. Reference: another PC publishes there ("Take over"). Follower: the reference changed
    /// (<see cref="ConfigurationSharingEngine.Candidate"/>, "Accept new reference").
    /// </summary>
    UnknownPublisher,

    /// <summary>Follower: the folder points at an older version than the one applied; it is ignored.</summary>
    RollbackRejected,

    /// <summary>Follower: the publication requires a newer Foundry version.</summary>
    Unsupported,

    /// <summary>
    /// Passwords must be entered again: the publication password or the reference keys are missing from the vault (for example
    /// after a DPAPI reset), or (follower) the applied configuration needs passwords the publication does not include.
    /// </summary>
    PasswordsNeeded
}

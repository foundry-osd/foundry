// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// The signed payload published by Foundry OSD at <c>&lt;share&gt;\Foundry\Index\&lt;keyId&gt;.json</c>: the custom image,
/// custom driver and post-installation sections Foundry Deploy applies when the network cache is on.
/// </summary>
public sealed record NetworkCacheContentIndex
{
    /// <summary>Gets the payload schema version; readers accept only 1.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Gets the fingerprint of the signing key, as computed by <see cref="PublicationSigner.KeyId"/>.</summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>Gets the publication counter; it grows on every publication so readers can refuse a replayed older index.</summary>
    public long Sequence { get; init; }

    /// <summary>Gets the publication time. It is informational only: the clock of a WinPE machine is not trusted, so it is never used to judge freshness.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the custom image section.</summary>
    public DeployCustomImagesSettings CustomImages { get; init; } = new();

    /// <summary>Gets the custom driver section.</summary>
    public DeployCustomDriversSettings CustomDrivers { get; init; } = new();

    /// <summary>Gets the post-installation section.</summary>
    public DeployPreOobeSettings PreOobe { get; init; } = new();
}

/// <summary>Outcome of reading a content index file.</summary>
public enum NetworkCacheIndexReadStatus
{
    /// <summary>The index is signed by the trusted key, supported and not older than the required sequence.</summary>
    Valid,

    /// <summary>No index file exists. Reported by callers that found no file; the codec never returns it for bytes it was given.</summary>
    Missing,

    /// <summary>The file exceeds <see cref="NetworkCacheContentIndexCodec.MaximumBytes"/> and was not parsed.</summary>
    TooLarge,

    /// <summary>The file is not a well-formed index envelope and payload.</summary>
    Malformed,

    /// <summary>The signature does not verify against the trusted key.</summary>
    SignatureInvalid,

    /// <summary>The payload names a key other than the trusted one.</summary>
    UnexpectedKey,

    /// <summary>The payload schema version is not supported by this Foundry version.</summary>
    UnsupportedSchema,

    /// <summary>The sequence is lower than the minimum the caller already accepted, which indicates a replayed older index.</summary>
    TooOld
}

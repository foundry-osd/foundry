// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Describes the SMB shares Deploy may use as a shared download cache and the IP scopes that select them.</summary>
public sealed record DeployNetworkCacheSettings
{
    public bool IsEnabled { get; init; }
    public IReadOnlyList<DeployNetworkCacheShare> Shares { get; init; } = [];
    /// <summary>Gets the scopes in authoring order; the most specific matching scope wins and ties keep this order.</summary>
    public IReadOnlyList<DeployNetworkCacheScope> Scopes { get; init; } = [];
    /// <summary>
    /// Gets the base64 DER SubjectPublicKeyInfo of the key that signs the content index published on the shares. Set when the
    /// section is active: custom images, driver sets and post-installation settings then come from that index, not the boot image.
    /// </summary>
    public string? PublisherPublicKey { get; init; }
    /// <summary>Gets the fingerprint of <see cref="PublisherPublicKey"/>, which names the index file on each share.</summary>
    public string? PublisherKeyId { get; init; }
    /// <summary>Gets the lowest index sequence Deploy accepts, so a stale index published before this media cannot be replayed.</summary>
    public long MinimumIndexSequence { get; init; }
}

/// <summary>Carries one share and, when it has an account, the credentials bound to that share and account.</summary>
public sealed record DeployNetworkCacheShare
{
    public string Id { get; init; } = string.Empty;
    /// <summary>Gets the UNC path of the share, for example <c>\\server\share</c>.</summary>
    public string Path { get; init; } = string.Empty;
    /// <summary>Gets the account used to connect. Deploy never writes to a share without an account.</summary>
    public string? AccountName { get; init; }
    /// <summary>Gets the payload that only decodes for this share path and this account.</summary>
    public SecretEnvelope? EncryptedCredentials { get; init; }
}

/// <summary>Maps an IP scope to the shares a device inside it tries, in order.</summary>
public sealed record DeployNetworkCacheScope
{
    public NetworkCacheScopeKind Kind { get; init; }
    /// <summary>Gets the scope text: CIDR for <see cref="NetworkCacheScopeKind.Subnet"/>, <c>first-last</c> for a range, or one address.</summary>
    public string Value { get; init; } = string.Empty;
    public IReadOnlyList<string> ShareIds { get; init; } = [];
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Sharing;

/// <summary>What a configuration package is used for.</summary>
public enum PackageKind
{
    /// <summary>An export file: unsigned, may embed small authoring files.</summary>
    Export,

    /// <summary>A publication manifest: signed by the reference PC, carries the content master key.</summary>
    Publication
}

/// <summary>Kind of library content a package references (JSON <c>file</c>, <c>package</c>, <c>driverSet</c>, <c>image</c>).</summary>
public enum SharedContentKind
{
    /// <summary>An authoring file such as a Wi-Fi profile template or a certificate; embedded in exports.</summary>
    File,

    /// <summary>A post-installation package; published, never exported.</summary>
    Package,

    /// <summary>A custom driver set; published, never exported.</summary>
    DriverSet,

    /// <summary>A custom image; published, never exported.</summary>
    Image
}

/// <summary>The reference PC that signed a publication.</summary>
/// <param name="Id">Stable publisher id.</param>
/// <param name="Name">Display name of the reference PC.</param>
/// <param name="KeyId">Fingerprint of <paramref name="PublicKey"/>, see <see cref="PublicationSigner.KeyId"/>.</param>
/// <param name="PublicKey">Signing public key as a DER SubjectPublicKeyInfo.</param>
public sealed record PackagePublisher(Guid Id, string Name, string KeyId, byte[] PublicKey);

/// <summary>One library content item the configuration references.</summary>
/// <param name="Hash">Lowercase SHA-256 content hash.</param>
/// <param name="Kind">Library the content belongs to.</param>
/// <param name="Length">Content length in bytes.</param>
/// <param name="Name">Display name of the content.</param>
public sealed record PackageContent(string Hash, SharedContentKind Kind, long Length, string Name);

/// <summary>
/// Decrypted content of a configuration package (spec 5.3). The caller owns the secret values, files and master key
/// it holds; nothing here is ever logged.
/// </summary>
public sealed record ConfigurationPackagePayload
{
    /// <summary>Payload format version written by this Foundry version.</summary>
    internal const int CurrentPayloadVersion = 1;

    /// <summary>Gets the payload format version.</summary>
    public int PayloadVersion { get; init; } = CurrentPayloadVersion;

    /// <summary>Gets whether this is an export or a publication.</summary>
    public required PackageKind Kind { get; init; }

    /// <summary>Gets when the package was written.</summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Gets the publication sequence; followers apply only a higher sequence.</summary>
    public long Sequence { get; init; }

    /// <summary>Gets the name shown when the package is imported; set on exports (publications name their <see cref="Publisher"/>).</summary>
    public string? Name { get; init; }

    /// <summary>Gets the signing reference PC; required on publications.</summary>
    public PackagePublisher? Publisher { get; init; }

    /// <summary>Gets the portable configuration; migrated to the current schema when read.</summary>
    public required FoundryConfigurationDocument Configuration { get; init; }

    /// <summary>Gets vault entries by secret id; present only when the user chose to include passwords.</summary>
    public IReadOnlyDictionary<string, string>? Secrets { get; init; }

    /// <summary>Gets the library content the configuration references.</summary>
    public IReadOnlyList<PackageContent> Contents { get; init; } = [];

    /// <summary>Gets embedded small files by content hash; present only in exports.</summary>
    public IReadOnlyDictionary<string, byte[]>? Files { get; init; }

    /// <summary>Gets the key that content keys are derived from; present only in publications.</summary>
    public byte[]? MasterKey { get; init; }
}

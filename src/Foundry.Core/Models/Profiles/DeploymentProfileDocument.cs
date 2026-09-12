// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.Profiles;

/// <summary>A portable authoring revision. Included secret and asset buffers belong to its owner and must be cleared after use.</summary>
public sealed record DeploymentProfileDocument
{
    public const int CurrentFormatVersion = 1;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    /// <summary>Shared identity preserved when joining; independent copies must allocate a new identity.</summary>
    public Guid ProfileId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public FoundryConfigurationDocument Configuration { get; init; } = new();
    public DeploymentProfileSecrets Secrets { get; init; } = new();
    public IReadOnlyList<DeploymentProfileAsset> Assets { get; init; } = [];
}

/// <summary>Logical secret records; native credential target names are never portable.</summary>
public sealed record DeploymentProfileSecrets
{
    public IReadOnlyList<DeploymentProfileSecret> Entries { get; init; } = [];
}

/// <summary>A secret bound to a product purpose and stable context identity, encoded as UTF-8 by the caller.</summary>
public sealed record DeploymentProfileSecret
{
    public ProfileSecretPurpose Purpose { get; init; }
    /// <summary>A context identifier of 1 to 128 ASCII letters, digits, hyphens, underscores or dots.</summary>
    public string Identity { get; init; } = string.Empty;
    public ProfileValueState State { get; init; }
    /// <summary>Owned secret bytes, required only for Present. Blank and all absent states use null.</summary>
    public byte[]? Value { get; init; }
}

/// <summary>An exact source file with a verified digest and managed relative destination, never an imported host path.</summary>
public sealed record DeploymentProfileAsset
{
    public string Id { get; init; } = string.Empty;
    public ProfileAssetKind Kind { get; init; }
    /// <summary>Case-insensitively unique portable destination with safe ASCII path segments and forward slashes.</summary>
    public string RelativePath { get; init; } = string.Empty;
    public ProfileValueState State { get; init; }
    /// <summary>SHA-256 hexadecimal source digest, mandatory for present bytes and verified before activation.</summary>
    public string? Sha256 { get; init; }
    public byte[]? Content { get; init; }
}

/// <summary>Omission and failed lookup never imply deletion or an intentionally empty password.</summary>
public enum ProfileValueState { Omitted, Unavailable, Deleted, Blank, Present }

/// <summary>Allowlisted password families, independent of operating-system credential storage names.</summary>
public enum ProfileSecretPurpose { DeploymentPassword, WifiPassphrase, AdministratorPassword, AdditionalAccountPassword, WiredCertificatePassword, WifiCertificatePassword, AutopilotCertificatePassword, SharedProfileKey }

/// <summary>Allowlisted portable inputs. Opaque files are included only when the caller explicitly supplies their bytes.</summary>
public enum ProfileAssetKind { Unattend, WiredProfile, WifiProfile, WiredCertificate, WifiCertificate, AutopilotProfile, AutopilotCertificate }

/// <summary>Distinct authenticated domains prevent a local ciphertext being accepted as a shared revision.</summary>
public enum ProfilePackagePurpose { LocalStorage = 2, SharedRevision = 3 }

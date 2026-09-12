// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;

namespace Foundry.Core.Services.Profiles;

/// <summary>Validates the entire bounded payload before it can be activated or persisted.</summary>
internal static class DeploymentProfilePayload
{
    internal const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private const int MaximumAssetBytes = 4 * 1024 * 1024;
    private const int MaximumTotalAssetBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding SecretEncoding = new(false, true);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    internal static byte[] Serialize(DeploymentProfileDocument profile, bool portable)
    {
        Validate(profile);
        DeploymentProfileDocument projected = profile with { Configuration = ProjectConfiguration(profile.Configuration, portable) };
        using var buffer = new BoundedPayloadStream();
        try
        {
            JsonSerializer.Serialize(buffer, projected, Options);
            return buffer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
        }
    }
    internal static DeploymentProfileDocument Deserialize(byte[] bytes, bool portable)
    {
        DeploymentProfileDocument? profile = null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            ValidateJson(json.RootElement);
            RequireProperties(json.RootElement, "formatVersion", "profileId", "displayName", "configuration", "secrets", "assets");
            if (json.RootElement.GetProperty("formatVersion").GetInt32() != DeploymentProfileDocument.CurrentFormatVersion)
            {
                throw new InvalidDataException("The profile payload format is unsupported.");
            }
            JsonElement configuration = json.RootElement.GetProperty("configuration");
            RequireProperties(configuration, "schemaVersion");
            int schema = configuration.GetProperty("schemaVersion").GetInt32();
            if (schema < 1 || schema > FoundryConfigurationDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException("The profile authoring schema is unsupported.");
            }
            foreach (JsonElement secret in json.RootElement.GetProperty("secrets").GetProperty("entries").EnumerateArray())
            {
                RequireProperties(secret, "purpose", "identity", "state");
            }
            foreach (JsonElement asset in json.RootElement.GetProperty("assets").EnumerateArray())
            {
                RequireProperties(asset, "id", "kind", "relativePath", "state");
            }
            profile = JsonSerializer.Deserialize<DeploymentProfileDocument>(bytes, Options)
                ?? throw new InvalidDataException("The profile payload is missing.");
            Validate(profile);
            return profile with { Configuration = ProjectConfiguration(profile.Configuration, portable) };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            ClearBuffers(profile);
            throw new InvalidDataException("The profile payload is invalid.", exception);
        }
        catch
        {
            ClearBuffers(profile);
            throw;
        }
    }

    private static FoundryConfigurationDocument ProjectConfiguration(FoundryConfigurationDocument configuration, bool portable)
    {
        FoundryConfigurationDocument projected = DeploymentProfileProjection.CreatePortable(configuration);
        return portable ? projected : projected with
        {
            General = configuration.General,
            Network = projected.Network with
            {
                Wifi = projected.Network.Wifi with { CertificatePath = configuration.Network.Wifi.CertificatePath, EnterpriseProfileTemplatePath = configuration.Network.Wifi.EnterpriseProfileTemplatePath },
                Dot1x = projected.Network.Dot1x with { CertificatePath = configuration.Network.Dot1x.CertificatePath, ProfileTemplatePath = configuration.Network.Dot1x.ProfileTemplatePath }
            },
            Unattend = configuration.Unattend,
            Telemetry = configuration.Telemetry
        };
    }
    private static void Validate(DeploymentProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.FormatVersion != DeploymentProfileDocument.CurrentFormatVersion || profile.ProfileId == Guid.Empty
            || string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 128 || profile.DisplayName.Any(char.IsControl)
            || profile.Configuration is null || profile.Secrets?.Entries is null || profile.Assets is null
            || profile.Secrets.Entries.Count > 128 || profile.Assets.Count > 64)
        {
            throw new InvalidDataException("The profile metadata is invalid or unsupported.");
        }
        _ = DeploymentProfileProjection.CreatePortable(profile.Configuration);
        ValidateConfigurationCollections(profile.Configuration);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeploymentProfileSecret secret in profile.Secrets.Entries)
        {
            if (secret is null || !Enum.IsDefined(secret.Purpose) || !Enum.IsDefined(secret.State) || !IsIdentity(secret.Identity)
                || !identities.Add($"{(int)secret.Purpose}:{secret.Identity}")
                || (secret.State == ProfileValueState.Present ? !IsValidSecret(secret.Value) : secret.Value is not null))
            {
                throw new InvalidDataException("The profile secret record is invalid.");
            }
        }
        identities.Clear();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (DeploymentProfileAsset asset in profile.Assets)
        {
            if (asset is null || !Enum.IsDefined(asset.Kind) || !Enum.IsDefined(asset.State) || asset.State == ProfileValueState.Blank
                || !IsIdentity(asset.Id) || !identities.Add(asset.Id) || !IsSafePath(asset.RelativePath) || !paths.Add(asset.RelativePath))
            {
                throw new InvalidDataException("The profile asset record is invalid.");
            }
            if (asset.State == ProfileValueState.Present)
            {
                if (asset.Content is not { Length: <= MaximumAssetBytes } || !IsDigest(asset.Sha256)
                    || !string.Equals(Convert.ToHexString(SHA256.HashData(asset.Content)), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The profile asset content or digest is invalid.");
                }
                totalBytes += asset.Content.Length;
            }
            else if (asset.Content is not null || (asset.Sha256 is not null && !IsDigest(asset.Sha256)))
            {
                throw new InvalidDataException("An absent asset cannot contain bytes or an invalid digest.");
            }
        }
        if (totalBytes > MaximumTotalAssetBytes)
        {
            throw new InvalidDataException("The profile assets exceed the total size limit.");
        }
        ValidateSourceAssets(profile);
        identities.Clear();
        foreach (OobeAdditionalAccountSettings account in profile.Configuration.Customization.Oobe.AdditionalAccounts)
        {
            if (account is null || !IsIdentity(account.Id) || !identities.Add(account.Id))
            {
                throw new InvalidDataException("The profile account identity is invalid or duplicated.");
            }
        }
    }

    private static bool IsValidSecret(byte[]? value)
    {
        if (value is not { Length: > 0 and <= 2560 })
        {
            return false;
        }
        try
        {
            _ = SecretEncoding.GetCharCount(value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void ValidateConfigurationCollections(FoundryConfigurationDocument configuration)
    {
        ValidateList(configuration.Customization.MachineNaming.Components);
        ValidateList(configuration.Customization.Oobe.AdditionalAccounts);
        ValidateList(configuration.Customization.AppxRemoval.PackageNames);
        ValidateList(configuration.Customization.WindowsOptionalFeatures.EnabledFeatureIds);
        ValidateList(configuration.Customization.WindowsOptionalFeatures.DisabledFeatureIds);
        ValidateList(configuration.OperatingSystemSelection.AllowedLanguageCodes);
        ValidateList(configuration.OperatingSystemSelection.AllowedReleaseIds);
        ValidateList(configuration.OperatingSystemSelection.AllowedLicenseChannels);
        ValidateList(configuration.OperatingSystemSelection.AllowedEditions);
        ValidateList(configuration.Autopilot.HardwareHashUpload.KnownGroupTags);
        ValidateList(configuration.Autopilot.Profiles);
        ValidateList(configuration.Unattend.Files);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AutopilotProfileSettings autopilot in configuration.Autopilot.Profiles)
        {
            if (!IsIdentity(autopilot.Id) || !ids.Add(autopilot.Id) || !IsSafeFolder(autopilot.FolderName) || !folders.Add(autopilot.FolderName)
                || string.IsNullOrWhiteSpace(autopilot.DisplayName) || string.IsNullOrWhiteSpace(autopilot.Source) || string.IsNullOrWhiteSpace(autopilot.JsonContent))
            {
                throw new InvalidDataException("The Autopilot profile metadata is invalid or duplicated.");
            }
        }
    }

    private static void ValidateList<T>(IReadOnlyList<T>? values) where T : class
    {
        if (values is null || values.Count > 1024 || values.Any(value => value is null))
        {
            throw new InvalidDataException("The profile configuration collection is invalid or too large.");
        }
    }

    private static bool IsSafeFolder(string? folder)
    {
        if (folder is not { Length: > 0 and <= 200 } || folder is "." or ".." || folder.EndsWith('.') || folder.EndsWith(' ')
            || folder.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
        {
            return false;
        }
        string name = folder.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return name is not ("CON" or "PRN" or "AUX" or "NUL")
            && !(name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '0' and <= '9');
    }
    private static void ValidateSourceAssets(DeploymentProfileDocument profile)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UnattendFileSettings file in profile.Configuration.Unattend.Files)
        {
            if (file.Id is not { Length: 32 } || !file.Id.All(char.IsAsciiHexDigit) || !ids.Add(file.Id)
                || !IsDigest(file.ContentHash) || !hashes.Add(file.ContentHash)
                || string.IsNullOrWhiteSpace(file.DisplayName) || file.DisplayName.Length > 200 || file.DisplayName.Any(char.IsControl))
            {
                throw new InvalidDataException("The answer-file source metadata is invalid or duplicated.");
            }
            DeploymentProfileAsset? asset = profile.Assets.FirstOrDefault(candidate => candidate.Kind == ProfileAssetKind.Unattend
                && string.Equals(candidate.Id, file.Id, StringComparison.OrdinalIgnoreCase));
            if (asset?.State == ProfileValueState.Present && !string.Equals(file.ContentHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The answer-file asset does not match its recorded source digest.");
            }
        }
    }
    private static bool IsIdentity(string? identity) => identity is { Length: > 0 and <= 128 } and not "." and not ".."
        && identity.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsDigest(string? digest) => digest is { Length: 64 } && digest.All(char.IsAsciiHexDigit);

    private static bool IsSafePath(string? path)
    {
        if (path is not { Length: > 0 and <= 200 } || path.Contains('\\'))
        {
            return false;
        }
        foreach (string segment in path.Split('/'))
        {
            if (!IsIdentity(segment) || segment is "." or ".." || segment.EndsWith('.'))
            {
                return false;
            }
            string name = segment.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL"
                || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '0' and <= '9'))
            {
                return false;
            }
        }
        return true;
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || names.Any(name => !element.TryGetProperty(name, out _)))
        {
            throw new InvalidDataException("Required profile properties are missing.");
        }
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate profile properties are not allowed.");
                }
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateJson(item);
            }
        }
    }

    private sealed class BoundedPayloadStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityLimit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityLimit(buffer.Length);
            base.Write(buffer);
        }

        private void EnsureCapacityLimit(int count)
        {
            if (Position + count > MaximumPayloadBytes)
            {
                throw new InvalidDataException("The profile payload exceeds the size limit.");
            }
        }
    }
    private static void ClearBuffers(DeploymentProfileDocument? profile)
    {
        if (profile?.Secrets?.Entries is { } secrets)
        {
            foreach (DeploymentProfileSecret secret in secrets)
            {
                if (secret?.Value is { } value)
                {
                    CryptographicOperations.ZeroMemory(value);
                }
            }
        }
        if (profile?.Assets is { } assets)
        {
            foreach (DeploymentProfileAsset asset in assets)
            {
                if (asset?.Content is { } content)
                {
                    CryptographicOperations.ZeroMemory(content);
                }
            }
        }
    }
}

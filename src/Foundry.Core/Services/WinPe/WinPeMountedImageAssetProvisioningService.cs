using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeMountedImageAssetProvisioningService : IWinPeMountedImageAssetProvisioningService
{
    private const string BootstrapFileName = "FoundryBootstrap.ps1";
    private const string BootstrapInvocation = @"powershell.exe -ExecutionPolicy Bypass -NoProfile -File X:\Windows\System32\FoundryBootstrap.ps1";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<WinPeResult> ProvisionAsync(
        WinPeMountedImageAssetProvisioningOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        try
        {
            string mountedImagePath = Path.GetFullPath(options.MountedImagePath);
            string system32Path = Path.Combine(mountedImagePath, "Windows", "System32");
            string foundryRootPath = Path.Combine(mountedImagePath, "Foundry");
            string foundryConfigPath = Path.Combine(foundryRootPath, "Config");

            Directory.CreateDirectory(system32Path);
            Directory.CreateDirectory(foundryConfigPath);
            Directory.CreateDirectory(Path.Combine(foundryRootPath, "Logs"));
            Directory.CreateDirectory(Path.Combine(foundryRootPath, "Temp"));

            await File.WriteAllTextAsync(
                Path.Combine(system32Path, BootstrapFileName),
                options.BootstrapScriptContent,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);

            File.Copy(options.CurlExecutableSourcePath, Path.Combine(system32Path, "curl.exe"), overwrite: true);

            ProvisionBundledSevenZip(mountedImagePath, options);
            await WriteStartnetAsync(system32Path, cancellationToken).ConfigureAwait(false);
            await WriteConfigurationAssetsAsync(mountedImagePath, foundryConfigPath, options, cancellationToken).ConfigureAwait(false);

            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry boot assets into the mounted WinPE image.",
                ex.Message);
        }
    }

    private static async Task WriteStartnetAsync(string system32Path, CancellationToken cancellationToken)
    {
        string startnetPath = Path.Combine(system32Path, "startnet.cmd");
        List<string> lines = File.Exists(startnetPath)
            ? [.. await File.ReadAllLinesAsync(startnetPath, cancellationToken).ConfigureAwait(false)]
            : [];

        if (!lines.Any(line => line.Trim().Equals("wpeinit", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Insert(0, "wpeinit");
        }

        if (!lines.Any(line => line.Contains(BootstrapFileName, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(BootstrapInvocation);
        }

        await File.WriteAllLinesAsync(startnetPath, lines, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteConfigurationAssetsAsync(
        string mountedImagePath,
        string foundryConfigPath,
        WinPeMountedImageAssetProvisioningOptions options,
        CancellationToken cancellationToken)
    {
        CreateNetworkAssetDirectories(foundryConfigPath);

        string connectConfigurationJson = string.IsNullOrWhiteSpace(options.FoundryConnectConfigurationJson)
            ? CreateFallbackFoundryConnectConfigurationJson()
            : options.FoundryConnectConfigurationJson;

        await File.WriteAllTextAsync(
            Path.Combine(foundryConfigPath, "foundry.connect.config.json"),
            connectConfigurationJson,
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        string deployConfigurationJson = string.IsNullOrWhiteSpace(options.ExpertDeployConfigurationJson)
            ? CreateFallbackDeployConfigurationJson()
            : options.ExpertDeployConfigurationJson;

        await File.WriteAllTextAsync(
            Path.Combine(foundryConfigPath, "foundry.deploy.config.json"),
            deployConfigurationJson,
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        await WriteMediaSecretsKeyAsync(
            foundryConfigPath,
            connectConfigurationJson,
            options.MediaSecretsKey,
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(foundryConfigPath, "foundry.connect.provisioning-source.txt"),
            FormatProvisioningSource(options.ConnectProvisioningSource),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(foundryConfigPath, "foundry.deploy.provisioning-source.txt"),
            FormatProvisioningSource(options.DeployProvisioningSource),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(foundryConfigPath, "iana-windows-timezones.json"),
            options.IanaWindowsTimeZoneMapJson,
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        CopyConnectAssetFiles(mountedImagePath, options.FoundryConnectAssetFiles);
        await WriteAutopilotProfilesAsync(foundryConfigPath, options.AutopilotProfiles, cancellationToken).ConfigureAwait(false);
    }

    private static void CreateNetworkAssetDirectories(string foundryConfigPath)
    {
        string networkRoot = Path.Combine(foundryConfigPath, "Network");
        Directory.CreateDirectory(Path.Combine(networkRoot, "Wired", "Profiles"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Wifi", "Profiles"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates", "Wired"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates", "Wifi"));
    }

    private static async Task WriteMediaSecretsKeyAsync(
        string foundryConfigPath,
        string connectConfigurationJson,
        byte[]? mediaSecretsKey,
        CancellationToken cancellationToken)
    {
        bool hasEncryptedSecrets = HasEncryptedSecrets(connectConfigurationJson);
        if (mediaSecretsKey is null || mediaSecretsKey.Length == 0)
        {
            if (hasEncryptedSecrets)
            {
                throw new ArgumentException("Foundry Connect encrypted secrets require a media secret key.");
            }

            return;
        }

        if (!hasEncryptedSecrets)
        {
            throw new ArgumentException("A media secret key must not be provisioned without encrypted Foundry Connect secrets.");
        }

        if (mediaSecretsKey.Length != 32)
        {
            throw new ArgumentException("Media secret key must be 32 bytes.");
        }

        string secretsPath = Path.Combine(foundryConfigPath, "Secrets");
        Directory.CreateDirectory(secretsPath);
        await File.WriteAllBytesAsync(
            Path.Combine(secretsPath, "media-secrets.key"),
            mediaSecretsKey,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool HasEncryptedSecrets(string connectConfigurationJson)
    {
        using JsonDocument document = JsonDocument.Parse(connectConfigurationJson);
        return HasEncryptedSecrets(document.RootElement);
    }

    private static bool HasEncryptedSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsEncryptedSecretEnvelope(element))
            {
                return true;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (HasEncryptedSecrets(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (HasEncryptedSecrets(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEncryptedSecretEnvelope(JsonElement element)
    {
        return HasStringProperty(element, "kind", "encrypted") &&
               HasStringProperty(element, "algorithm", "aes-gcm-v1") &&
               HasStringProperty(element, "keyId", "media") &&
               HasNonEmptyStringProperty(element, "nonce") &&
               HasNonEmptyStringProperty(element, "tag") &&
               HasNonEmptyStringProperty(element, "ciphertext");
    }

    private static bool HasStringProperty(JsonElement element, string propertyName, string expectedValue)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String &&
               string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);
    }

    private static bool HasNonEmptyStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString());
    }

    private static void CopyConnectAssetFiles(
        string mountedImagePath,
        IReadOnlyList<FoundryConnectProvisionedAssetFile> assetFiles)
    {
        foreach (FoundryConnectProvisionedAssetFile assetFile in assetFiles)
        {
            if (string.IsNullOrWhiteSpace(assetFile.SourcePath))
            {
                throw new ArgumentException("Foundry Connect asset source path is required.");
            }

            if (!File.Exists(assetFile.SourcePath))
            {
                throw new IOException($"Foundry Connect asset source file was not found: '{assetFile.SourcePath}'.");
            }

            string destinationPath = ResolveSafeRelativePath(mountedImagePath, assetFile.RelativeDestinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(assetFile.SourcePath, destinationPath, overwrite: true);
        }
    }

    private static void ProvisionBundledSevenZip(
        string mountedImagePath,
        WinPeMountedImageAssetProvisioningOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SevenZipSourceDirectoryPath))
        {
            return;
        }

        string runtimeFolder = options.Architecture.ToSevenZipRuntimeFolder();
        string sourceRootPath = options.SevenZipSourceDirectoryPath;
        string sourceExecutablePath = Path.Combine(sourceRootPath, runtimeFolder, "7za.exe");
        string sourceLicensePath = Path.Combine(sourceRootPath, "License.txt");
        string sourceReadmePath = Path.Combine(sourceRootPath, "readme.txt");

        if (!File.Exists(sourceExecutablePath) || !File.Exists(sourceLicensePath) || !File.Exists(sourceReadmePath))
        {
            throw new IOException($"Bundled 7-Zip assets are incomplete under '{sourceRootPath}' for runtime '{runtimeFolder}'.");
        }

        string destinationToolsRootPath = Path.Combine(mountedImagePath, "Foundry", "Tools", "7zip");
        string destinationRuntimePath = Path.Combine(destinationToolsRootPath, runtimeFolder);
        Directory.CreateDirectory(destinationRuntimePath);

        File.Copy(sourceExecutablePath, Path.Combine(destinationRuntimePath, "7za.exe"), overwrite: true);
        File.Copy(sourceLicensePath, Path.Combine(destinationToolsRootPath, "License.txt"), overwrite: true);
        File.Copy(sourceReadmePath, Path.Combine(destinationToolsRootPath, "readme.txt"), overwrite: true);
    }

    private static async Task WriteAutopilotProfilesAsync(
        string foundryConfigPath,
        IReadOnlyList<AutopilotProfileSettings> autopilotProfiles,
        CancellationToken cancellationToken)
    {
        foreach (AutopilotProfileSettings profile in autopilotProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.FolderName))
            {
                throw new ArgumentException("Autopilot profile folder name is required.");
            }

            string profilePath = ResolveSafeRelativePath(
                foundryConfigPath,
                Path.Combine("Autopilot", profile.FolderName, "AutopilotConfigurationFile.json"));

            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            await File.WriteAllTextAsync(profilePath, profile.JsonContent, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveSafeRelativePath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative destination path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"Relative destination path must not be rooted: '{relativePath}'.");
        }

        string fullRoot = Path.GetFullPath(rootPath);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string rootedPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Relative destination path escapes the mounted image: '{relativePath}'.");
        }

        return fullPath;
    }

    private static string FormatProvisioningSource(WinPeProvisioningSource source)
    {
        return source switch
        {
            WinPeProvisioningSource.Debug => "debug",
            WinPeProvisioningSource.Release => "release",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported provisioning source.")
        };
    }

    private static string CreateFallbackFoundryConnectConfigurationJson()
    {
        return JsonSerializer.Serialize(new FoundryConnectConfigurationDocument(), ConfigurationJsonDefaults.SerializerOptions);
    }

    private static string CreateFallbackDeployConfigurationJson()
    {
        return JsonSerializer.Serialize(new FoundryDeployConfigurationDocument(), ConfigurationJsonDefaults.SerializerOptions);
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeMountedImageAssetProvisioningOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image asset provisioning options are required.",
                "Provide a non-null WinPeMountedImageAssetProvisioningOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path is required.",
                "Set WinPeMountedImageAssetProvisioningOptions.MountedImagePath.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapScriptContent))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Foundry bootstrap script content is required.",
                "Set WinPeMountedImageAssetProvisioningOptions.BootstrapScriptContent.");
        }

        if (string.IsNullOrWhiteSpace(options.CurlExecutableSourcePath) || !File.Exists(options.CurlExecutableSourcePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "curl.exe source path is required.",
                $"Expected file: '{options.CurlExecutableSourcePath}'.");
        }

        if (string.IsNullOrWhiteSpace(options.IanaWindowsTimeZoneMapJson))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "IANA Windows time zone map JSON is required.",
                "Set WinPeMountedImageAssetProvisioningOptions.IanaWindowsTimeZoneMapJson.");
        }

        if (!Enum.IsDefined(options.ConnectProvisioningSource) || !Enum.IsDefined(options.DeployProvisioningSource))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Provisioning source value is invalid.",
                $"Connect: '{options.ConnectProvisioningSource}', Deploy: '{options.DeployProvisioningSource}'.");
        }

        return null;
    }
}

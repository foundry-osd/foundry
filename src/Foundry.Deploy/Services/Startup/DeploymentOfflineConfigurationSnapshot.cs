// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment.Unattend;

namespace Foundry.Deploy.Services.Startup;

/// <summary>Binds validated settings and readiness digest to the same bounded configuration bytes.</summary>
internal sealed class DeploymentOfflineConfigurationSnapshot
{
    private readonly byte[] content;
    public FoundryDeployConfigurationDocument Document { get; }
    public string Digest { get; }
    private DeploymentOfflineConfigurationSnapshot(byte[] content, FoundryDeployConfigurationDocument document)
    {
        this.content = content;
        Document = document;
        Digest = Convert.ToHexString(SHA256.HashData(content));
    }
    public byte[] CopyContent() => content.ToArray();

    public static DeploymentOfflineConfigurationSnapshot Parse(ReadOnlySpan<byte> input)
    {
        if (input.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Configuration size is invalid.");
        byte[] captured = input.ToArray();
        using var reader = new StreamReader(new MemoryStream(captured), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        string json = reader.ReadToEnd();
        ConfigurationVersionGuard.ThrowIfUnsupported(json, "Foundry.Deploy", ConfigurationSchemaVersions.DeployCurrent,
            ConfigurationJsonDefaults.SerializerOptions);
        FoundryDeployConfigurationDocument document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json,
            ConfigurationJsonDefaults.SerializerOptions) ?? throw new InvalidDataException("The deployment configuration is unavailable or invalid.");
        if (document.Unattend is null || document.Protection is null || document.Autopilot is null || document.OperatingSystemSelection is null)
            throw new InvalidDataException("The deployment configuration contains an invalid required section.");
        UnattendCatalog.Validate(document.Unattend, document.Protection.IsEnabled);
        document = DeployConfigurationMigration.ApplySchemaMigrations(document);
        return new(captured, document);
    }
}

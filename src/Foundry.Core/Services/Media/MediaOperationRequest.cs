// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Media;

public enum MediaOperationTarget { Iso, UsbCreate, UsbUpdate }

/// <summary>Owns captured configuration and volatile account secrets for a single operation.</summary>
public sealed class MediaConfigurationSnapshot : IDisposable
{
    public MediaConfigurationSnapshot(FoundryConfigurationDocument configuration,
        FoundryConfigurationDocument connectDocument, FoundryConfigurationDocument deployDocument,
        OobeAccountSecretState oobeAccountSecrets)
    {
        Configuration = Copy(configuration);
        ConnectDocument = Copy(connectDocument);
        DeployDocument = Copy(deployDocument);
        OobeAccountSecrets = oobeAccountSecrets;
    }

    public FoundryConfigurationDocument Configuration { get; }
    public FoundryConfigurationDocument ConnectDocument { get; }
    public FoundryConfigurationDocument DeployDocument { get; }
    public OobeAccountSecretState OobeAccountSecrets { get; }

    public void Dispose() => OobeAccountSecrets.Dispose();

    private static FoundryConfigurationDocument Copy(FoundryConfigurationDocument document)
    {
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(document, ConfigurationJsonDefaults.SerializerOptions);
        FoundryConfigurationDocument copy;
        try
        {
            copy = JsonSerializer.Deserialize<FoundryConfigurationDocument>(serialized,
                ConfigurationJsonDefaults.SerializerOptions)!;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialized);
        }
        return copy with
        {
            Network = copy.Network with
            {
                Dot1x = copy.Network.Dot1x with { CertificatePfxPassword = document.Network.Dot1x.CertificatePfxPassword },
                Wifi = copy.Network.Wifi with { CertificatePfxPassword = document.Network.Wifi.CertificatePfxPassword }
            },
            Autopilot = copy.Autopilot with
            {
                HardwareHashUpload = copy.Autopilot.HardwareHashUpload with
                {
                    BootMediaCertificate = document.Autopilot.HardwareHashUpload.BootMediaCertificate with { }
                }
            }
        };
    }
}

/// <summary>Captures the inputs before asynchronous media work starts; ownership transfers to the coordinator.</summary>
public sealed class MediaOperationRequest : IDisposable
{
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public required MediaOperationTarget Target { get; init; }
    public required MediaPreflightOptions Options { get; init; }
    public required MediaConfigurationSnapshot Configuration { get; init; }
    public required DeploymentMediaProtectionMaterial Protection { get; init; }
    public required WinPeRuntimePayloadProvisioningOptions RuntimePayloads { get; init; }
    public required string WorkspaceRoot { get; init; }
    public string? AdkRootPath { get; init; }
    public required string WinReCacheDirectoryPath { get; init; }
    public TelemetrySettings ConnectTelemetry { get; init; } = new();
    public TelemetrySettings DeployTelemetry { get; init; } = new();

    public void Dispose()
    {
        Configuration.Dispose();
        Protection.Dispose();
    }
}

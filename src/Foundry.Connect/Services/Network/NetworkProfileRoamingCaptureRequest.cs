using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Describes a Foundry-managed network profile capture request.
/// </summary>
public sealed record NetworkProfileRoamingCaptureRequest(
    string ProfilePath,
    NetworkProfileRoamingProfileSource Source,
    NetworkProfileRoamingConnectivityExpectation ConnectivityExpectation,
    IReadOnlyList<string>? CertificatePaths = null,
    SecretEnvelope? CertificatePfxPasswordSecret = null);

/// <summary>
/// Identifies the Foundry source that produced a captured profile.
/// </summary>
public enum NetworkProfileRoamingProfileSource
{
    ManualWifi,
    ProvisionedWifi,
    ProvisionedWiredDot1x
}

/// <summary>
/// Describes the expected pre-OOBE connectivity behavior after importing a captured profile.
/// </summary>
public enum NetworkProfileRoamingConnectivityExpectation
{
    PreOobeConnectable,
    ImportOnly,
    DependsOnMachineCredential
}

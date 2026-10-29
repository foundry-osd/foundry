// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Describes a Foundry-managed network profile capture request.
/// </summary>
/// <remarks>
/// The profile comes either from a file (<paramref name="ProfilePath"/>, such as a media template) or from XML read
/// back from WLAN (<paramref name="ProfileXml"/>), which wins when both are set. The XML may carry a plaintext key, so
/// the generated <see cref="ToString"/> never prints it.
/// </remarks>
/// <param name="ProfilePath">Source profile file, or <see langword="null"/> when <paramref name="ProfileXml"/> is given.</param>
/// <param name="Source">Foundry source that produced the profile.</param>
/// <param name="ConnectivityExpectation">Expected pre-OOBE connectivity after import.</param>
/// <param name="CertificatePaths">Certificates to roam with the profile.</param>
/// <param name="CertificatePfxPasswordSecret">Encrypted password of a PFX certificate.</param>
/// <param name="ProfileXml">Profile XML to write instead of copying <paramref name="ProfilePath"/>.</param>
public sealed record NetworkProfileRoamingCaptureRequest(
    string? ProfilePath,
    NetworkProfileRoamingProfileSource Source,
    NetworkProfileRoamingConnectivityExpectation ConnectivityExpectation,
    IReadOnlyList<string>? CertificatePaths = null,
    SecretEnvelope? CertificatePfxPasswordSecret = null,
    string? ProfileXml = null)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Source = ").Append(Source)
            .Append(", ConnectivityExpectation = ").Append(ConnectivityExpectation)
            .Append(", HasProfileXml = ").Append(ProfileXml is not null);
        return true;
    }
}

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

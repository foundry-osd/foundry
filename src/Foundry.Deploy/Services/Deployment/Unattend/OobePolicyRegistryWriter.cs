// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;
using static Foundry.Deploy.Services.Deployment.Unattend.OfflineRegistryWriter;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Writes Windows OOBE privacy policy values into offline registry hives.
/// </summary>
internal sealed class OobePolicyRegistryWriter
{
    private const string SoftwareHiveMount = @"HKLM\FoundrySoftware";
    private const string DefaultUserHiveMount = @"HKU\FoundryDefault";

    private readonly OfflineRegistryWriter _registryWriter;

    /// <summary>
    /// Initializes a registry writer that applies policy values with reg.exe.
    /// </summary>
    /// <param name="processRunner">The process runner used for reg.exe operations.</param>
    public OobePolicyRegistryWriter(IProcessRunner processRunner)
    {
        _registryWriter = new OfflineRegistryWriter(processRunner);
    }

    /// <summary>
    /// Applies OOBE-related policy values to the offline Windows installation.
    /// </summary>
    public async Task ApplyAsync(
        string windowsPartitionRoot,
        DeployOobeSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string softwareHivePath = Path.Combine(windowsPartitionRoot, "Windows", "System32", "config", "SOFTWARE");
        string defaultUserHivePath = Path.Combine(windowsPartitionRoot, "Users", "Default", "NTUSER.DAT");

        await _registryWriter
            .WithLoadedHiveAsync(
                SoftwareHiveMount,
                softwareHivePath,
                workingDirectory,
                (hive, token) => ApplySoftwarePoliciesAsync(hive, settings, token),
                cancellationToken)
            .ConfigureAwait(false);

        await _registryWriter
            .WithLoadedHiveAsync(
                DefaultUserHiveMount,
                defaultUserHivePath,
                workingDirectory,
                (hive, token) => ApplyDefaultUserPoliciesAsync(hive, settings, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplySoftwarePoliciesAsync(
        OfflineRegistryHive hive,
        DeployOobeSettings settings,
        CancellationToken cancellationToken)
    {
        await hive.AddDwordAsync(
            @"Policies\Microsoft\Windows\DataCollection",
            "AllowTelemetry",
            ToTelemetryValue(settings.DiagnosticDataLevel),
            cancellationToken).ConfigureAwait(false);
        await hive.AddDwordAsync(
            @"Policies\Microsoft\Windows\OOBE",
            "DisablePrivacyExperience",
            settings.HidePrivacySetup ? 1 : 0,
            cancellationToken).ConfigureAwait(false);
        await hive.AddDwordAsync(
            @"Policies\Microsoft\Windows\AdvertisingInfo",
            "DisabledByGroupPolicy",
            settings.AllowAdvertisingId ? 0 : 1,
            cancellationToken).ConfigureAwait(false);
        await hive.AddDwordAsync(
            @"Policies\Microsoft\InputPersonalization",
            "AllowInputPersonalization",
            settings.AllowOnlineSpeechRecognition ? 1 : 0,
            cancellationToken).ConfigureAwait(false);
        await hive.AddDwordAsync(
            @"Microsoft\Windows\CurrentVersion\Policies\TextInput",
            "AllowLinguisticDataCollection",
            settings.AllowInkingAndTypingDiagnostics ? 1 : 0,
            cancellationToken).ConfigureAwait(false);
        await hive.AddDwordAsync(
            @"Policies\Microsoft\Windows\AppPrivacy",
            "LetAppsAccessLocation",
            settings.LocationAccess == DeployOobeLocationAccessMode.ForceOff ? 2 : 0,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task ApplyDefaultUserPoliciesAsync(
        OfflineRegistryHive hive,
        DeployOobeSettings settings,
        CancellationToken cancellationToken)
    {
        return hive.AddDwordAsync(
            @"Software\Policies\Microsoft\Windows\CloudContent",
            "DisableTailoredExperiencesWithDiagnosticData",
            settings.AllowTailoredExperiences ? 0 : 1,
            cancellationToken);
    }

    private static int ToTelemetryValue(DeployOobeDiagnosticDataLevel value)
    {
        return value switch
        {
            DeployOobeDiagnosticDataLevel.Optional => 3,
            DeployOobeDiagnosticDataLevel.Off => 0,
            _ => 1
        };
    }
}

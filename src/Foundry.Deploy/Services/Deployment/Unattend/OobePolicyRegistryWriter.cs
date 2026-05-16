using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Writes Windows OOBE privacy policy values into offline registry hives.
/// </summary>
internal sealed class OobePolicyRegistryWriter
{
    private const string SoftwareHiveMount = @"HKLM\FoundrySoftware";
    private const string DefaultUserHiveMount = @"HKU\FoundryDefault";

    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a registry writer that applies policy values with reg.exe.
    /// </summary>
    public OobePolicyRegistryWriter(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
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

        await LoadHiveAsync(SoftwareHiveMount, softwareHivePath, workingDirectory, cancellationToken).ConfigureAwait(false);
        try
        {
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry",
                ToTelemetryValue(settings.DiagnosticDataLevel),
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Policies\Microsoft\Windows\OOBE",
                "DisablePrivacyExperience",
                settings.HidePrivacySetup ? 1 : 0,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Policies\Microsoft\Windows\AdvertisingInfo",
                "DisabledByGroupPolicy",
                settings.AllowAdvertisingId ? 0 : 1,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Policies\Microsoft\InputPersonalization",
                "AllowInputPersonalization",
                settings.AllowOnlineSpeechRecognition ? 1 : 0,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Microsoft\Windows\CurrentVersion\Policies\TextInput",
                "AllowLinguisticDataCollection",
                settings.AllowInkingAndTypingDiagnostics ? 1 : 0,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(
                $@"{SoftwareHiveMount}\Policies\Microsoft\Windows\AppPrivacy",
                "LetAppsAccessLocation",
                settings.LocationAccess == DeployOobeLocationAccessMode.ForceOff ? 2 : 0,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnloadHiveAsync(SoftwareHiveMount, workingDirectory, CancellationToken.None).ConfigureAwait(false);
        }

        await LoadHiveAsync(DefaultUserHiveMount, defaultUserHivePath, workingDirectory, cancellationToken).ConfigureAwait(false);
        try
        {
            await AddDwordAsync(
                $@"{DefaultUserHiveMount}\Software\Policies\Microsoft\Windows\CloudContent",
                "DisableTailoredExperiencesWithDiagnosticData",
                settings.AllowTailoredExperiences ? 0 : 1,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnloadHiveAsync(DefaultUserHiveMount, workingDirectory, CancellationToken.None).ConfigureAwait(false);
        }
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

    private Task LoadHiveAsync(string mountName, string hivePath, string workingDirectory, CancellationToken cancellationToken)
    {
        return RunRequiredAsync("reg.exe", ["LOAD", mountName, hivePath], workingDirectory, cancellationToken);
    }

    private Task UnloadHiveAsync(string mountName, string workingDirectory, CancellationToken cancellationToken)
    {
        return RunRequiredAsync("reg.exe", ["UNLOAD", mountName], workingDirectory, cancellationToken);
    }

    private Task AddDwordAsync(
        string keyPath,
        string valueName,
        int value,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RunRequiredAsync(
            "reg.exe",
            ["ADD", keyPath, "/v", valueName, "/t", "REG_DWORD", "/d", value.ToString(), "/f"],
            workingDirectory,
            cancellationToken);
    }

    private async Task RunRequiredAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
    }
}

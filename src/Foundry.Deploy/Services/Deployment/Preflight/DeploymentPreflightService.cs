// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Applies the deployment readiness policy for the Windows 11-only workflow.
/// </summary>
public sealed class DeploymentPreflightService : IDeploymentPreflightService
{
    public const ulong RecommendedDiskSizeBytes = 64UL * 1024UL * 1024UL * 1024UL;

    public DeploymentPreflightResult Evaluate(
        HardwareProfile? hardware,
        TargetDiskInfo? targetDisk,
        OperatingSystemCatalogItem? operatingSystem)
    {
        var findings = new List<DeploymentPreflightFinding>();

        EvaluateHardware(hardware, operatingSystem, findings);
        EvaluateDisk(targetDisk, findings);

        return new DeploymentPreflightResult { Findings = findings };
    }

    private static void EvaluateHardware(
        HardwareProfile? hardware,
        OperatingSystemCatalogItem? operatingSystem,
        ICollection<DeploymentPreflightFinding> findings)
    {
        if (hardware?.FirmwareMode != FirmwareMode.Uefi)
        {
            findings.Add(Blocking(DeploymentPreflightFindingCodes.FirmwareMode, "Preflight.FirmwareModeBlocking"));
        }

        if (hardware is not null &&
            operatingSystem is not null &&
            !ArchitecturesMatch(hardware.Architecture, operatingSystem.Architecture))
        {
            findings.Add(Blocking(
                DeploymentPreflightFindingCodes.ArchitectureMismatch,
                "Preflight.ArchitectureMismatchBlocking",
                NormalizeArchitecture(operatingSystem.Architecture),
                NormalizeArchitecture(hardware.Architecture)));
        }

        if (hardware?.IsTpmPresent != true)
        {
            findings.Add(Blocking(DeploymentPreflightFindingCodes.TpmMissing, "Preflight.TpmMissingBlocking"));
        }
        else if (!IsTpm2(hardware.TpmSpecVersion))
        {
            findings.Add(Blocking(
                DeploymentPreflightFindingCodes.TpmVersion,
                "Preflight.TpmVersionBlocking",
                hardware.TpmSpecVersion));
        }
        else if (!hardware.IsTpmEnabled)
        {
            findings.Add(Blocking(DeploymentPreflightFindingCodes.TpmDisabled, "Preflight.TpmDisabledBlocking"));
        }
        else if (!hardware.IsTpmActivated)
        {
            findings.Add(Blocking(DeploymentPreflightFindingCodes.TpmDeactivated, "Preflight.TpmDeactivatedBlocking"));
        }

        switch (hardware?.SecureBootState)
        {
            case SecureBootState.Enabled:
                break;
            case SecureBootState.Disabled:
                findings.Add(Warning(DeploymentPreflightFindingCodes.SecureBootDisabled, "Preflight.SecureBootDisabledWarning"));
                break;
            default:
                findings.Add(Blocking(DeploymentPreflightFindingCodes.SecureBootUnavailable, "Preflight.SecureBootUnavailableBlocking"));
                break;
        }
    }

    private static void EvaluateDisk(
        TargetDiskInfo? targetDisk,
        ICollection<DeploymentPreflightFinding> findings)
    {
        if (targetDisk is null || targetDisk.SizeBytes == 0)
        {
            findings.Add(Blocking(DeploymentPreflightFindingCodes.DiskSizeUnknown, "Preflight.DiskSizeUnknownBlocking"));
            return;
        }

        if (targetDisk.SizeBytes < RecommendedDiskSizeBytes)
        {
            findings.Add(Warning(
                DeploymentPreflightFindingCodes.DiskBelowRecommendedSize,
                "Preflight.DiskBelowRecommendedSizeWarning",
                FormatGiB(targetDisk.SizeBytes),
                FormatGiB(RecommendedDiskSizeBytes)));
        }
    }

    private static bool ArchitecturesMatch(string hardwareArchitecture, string operatingSystemArchitecture)
    {
        string hardware = NormalizeArchitecture(hardwareArchitecture);
        string operatingSystem = NormalizeArchitecture(operatingSystemArchitecture);
        return hardware.Length > 0 &&
               operatingSystem.Length > 0 &&
               hardware.Equals(operatingSystem, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchitecture(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "amd64" => "x64",
            "aarch64" => "arm64",
            string normalized => normalized
        };
    }

    private static bool IsTpm2(string specVersion)
    {
        string primaryVersion = specVersion
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return Version.TryParse(primaryVersion, out Version? version) && version.Major >= 2;
    }

    private static string FormatGiB(ulong bytes)
    {
        return $"{bytes / 1024d / 1024d / 1024d:0.0}";
    }

    private static DeploymentPreflightFinding Blocking(string code, string messageResourceKey, params string[] arguments)
    {
        return CreateFinding(code, DeploymentPreflightSeverity.Blocking, messageResourceKey, arguments);
    }

    private static DeploymentPreflightFinding Warning(string code, string messageResourceKey, params string[] arguments)
    {
        return CreateFinding(code, DeploymentPreflightSeverity.Warning, messageResourceKey, arguments);
    }

    private static DeploymentPreflightFinding CreateFinding(
        string code,
        DeploymentPreflightSeverity severity,
        string messageResourceKey,
        IReadOnlyList<string> arguments)
    {
        return new DeploymentPreflightFinding
        {
            Code = code,
            Severity = severity,
            MessageResourceKey = messageResourceKey,
            MessageArguments = arguments
        };
    }
}

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Evaluates whether the current media configuration can produce ISO or USB output.
/// </summary>
public static class MediaPreflightService
{
    /// <summary>
    /// Evaluates media creation readiness for ISO and USB targets.
    /// </summary>
    /// <param name="options">The preflight options collected from app state.</param>
    /// <returns>The preflight evaluation including blocking reasons for each target.</returns>
    public static MediaPreflightEvaluation Evaluate(MediaPreflightOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<MediaPreflightBlockingReason> sharedReasons = GetSharedBlockingReasons(options);
        List<MediaPreflightBlockingReason> isoReasons = [.. sharedReasons];
        List<MediaPreflightBlockingReason> usbReasons = [.. sharedReasons];

        if (!IsIsoPathValid(options.IsoOutputPath))
        {
            isoReasons.Add(MediaPreflightBlockingReason.InvalidIsoPath);
        }

        if (options.SelectedUsbDisk is null)
        {
            usbReasons.Add(MediaPreflightBlockingReason.NoUsbTarget);
        }

        UsbPartitionStyle effectiveUsbPartitionStyle = options.UsbPartitionStyle;
        if (options.Architecture == WinPeArchitecture.Arm64 && options.UsbPartitionStyle == UsbPartitionStyle.Mbr)
        {
            // ARM64 boot media requires GPT even when the persisted USB preference still says MBR.
            effectiveUsbPartitionStyle = UsbPartitionStyle.Gpt;
            usbReasons.Add(MediaPreflightBlockingReason.Arm64RequiresGpt);
        }

        if (!string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath))
        {
            if (!Directory.Exists(options.CustomDriverDirectoryPath))
            {
                isoReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryNotFound);
                usbReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryNotFound);
            }
            else if (!Directory.EnumerateFiles(options.CustomDriverDirectoryPath, "*.inf", SearchOption.AllDirectories).Any())
            {
                isoReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);
                usbReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);
            }
        }

        bool canGenerateIsoSummary = !isoReasons.Except(GetDryRunAllowedBlockingReasons()).Any();
        bool canGenerateUsbSummary = !usbReasons.Except(
            [.. GetDryRunAllowedBlockingReasons(), MediaPreflightBlockingReason.Arm64RequiresGpt]).Any();

        return new MediaPreflightEvaluation
        {
            CanGenerateIsoSummary = canGenerateIsoSummary,
            CanGenerateUsbSummary = canGenerateUsbSummary,
            CanCreateIso = canGenerateIsoSummary && options.IsFinalExecutionEnabled && !isoReasons.Any(),
            CanCreateUsb = canGenerateUsbSummary && options.IsFinalExecutionEnabled && !usbReasons.Any(),
            EffectiveUsbPartitionStyle = effectiveUsbPartitionStyle,
            IsoBlockingReasons = isoReasons,
            UsbBlockingReasons = usbReasons
        };
    }

    private static List<MediaPreflightBlockingReason> GetSharedBlockingReasons(MediaPreflightOptions options)
    {
        List<MediaPreflightBlockingReason> reasons = [];

        if (!options.IsAdkReady)
        {
            reasons.Add(MediaPreflightBlockingReason.AdkNotReady);
        }

        if (!options.IsNetworkConfigurationReady)
        {
            reasons.Add(MediaPreflightBlockingReason.NetworkConfigurationNotReady);
        }

        if (!options.IsDeployConfigurationReady)
        {
            reasons.Add(MediaPreflightBlockingReason.DeployConfigurationNotReady);
        }

        if (!options.IsConnectProvisioningReady)
        {
            reasons.Add(MediaPreflightBlockingReason.ConnectProvisioningNotReady);
        }

        if (!options.AreRequiredSecretsReady)
        {
            reasons.Add(MediaPreflightBlockingReason.RequiredSecretsNotReady);
        }

        if (options.IsAutopilotEnabled && !options.IsAutopilotConfigurationReady)
        {
            reasons.Add(MediaPreflightBlockingReason.AutopilotConfigurationNotReady);
        }

        if (string.IsNullOrWhiteSpace(options.WinPeLanguage))
        {
            reasons.Add(MediaPreflightBlockingReason.MissingWinPeLanguage);
        }
        else if (options.AvailableWinPeLanguages.Count > 0
            && !options.AvailableWinPeLanguages.Contains(options.WinPeLanguage, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add(MediaPreflightBlockingReason.WinPeLanguageUnavailable);
        }

        if (!options.IsFinalExecutionEnabled)
        {
            reasons.Add(MediaPreflightBlockingReason.FinalExecutionDeferred);
        }

        return reasons;
    }

    private static bool IsIsoPathValid(string isoOutputPath)
    {
        if (string.IsNullOrWhiteSpace(isoOutputPath))
        {
            return false;
        }

        return string.Equals(Path.GetExtension(isoOutputPath), ".iso", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MediaPreflightBlockingReason> GetDryRunAllowedBlockingReasons()
    {
        return
        [
            MediaPreflightBlockingReason.NetworkConfigurationNotReady,
            MediaPreflightBlockingReason.DeployConfigurationNotReady,
            MediaPreflightBlockingReason.ConnectProvisioningNotReady,
            MediaPreflightBlockingReason.RequiredSecretsNotReady,
            MediaPreflightBlockingReason.FinalExecutionDeferred
        ];
    }
}

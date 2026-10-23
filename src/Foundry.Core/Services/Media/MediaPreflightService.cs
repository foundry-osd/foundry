// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.IO;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Evaluates whether the current media configuration can produce ISO or USB output.
/// </summary>
public static class MediaPreflightService
{
    /// <summary>
    /// Evaluates media creation readiness for ISO, USB and PXE targets.
    /// </summary>
    /// <param name="options">The preflight options collected from app state.</param>
    /// <returns>The preflight evaluation including blocking reasons for each target.</returns>
    public static MediaPreflightEvaluation Evaluate(MediaPreflightOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<MediaPreflightBlockingReason> sharedReasons = GetSharedBlockingReasons(options);
        List<MediaPreflightBlockingReason> isoReasons = [.. sharedReasons];
        List<MediaPreflightBlockingReason> usbReasons = [.. sharedReasons];
        // PXE shares the common rules and the custom driver checks; it has no ISO path or USB target.
        List<MediaPreflightBlockingReason> pxeReasons = [.. sharedReasons];

        if (!IsIsoPathValid(options.IsoOutputPath))
        {
            isoReasons.Add(MediaPreflightBlockingReason.InvalidIsoPath);
        }

        if (options.SelectedUsbDisk is null)
        {
            usbReasons.Add(MediaPreflightBlockingReason.NoUsbTarget);
        }
        else if (options.SelectedUsbDisk.SizeBytes < WinPeUsbMediaService.MinimumUsbDiskSizeBytes)
        {
            usbReasons.Add(MediaPreflightBlockingReason.UsbTargetBelowMinimumSize);
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
                pxeReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryNotFound);
            }
            else if (!FileSearch.ContainsRecursive(options.CustomDriverDirectoryPath, "*.inf"))
            {
                isoReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);
                usbReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);
                pxeReasons.Add(MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);
            }
        }

        // A drive without cache only has to hold the media, which USB creation measures before erasing the drive.
        List<MediaPreflightBlockingReason> usbWithoutCacheReasons =
            [.. usbReasons.Where(reason => reason != MediaPreflightBlockingReason.UsbTargetBelowMinimumSize)];

        bool canGenerateIsoSummary =!isoReasons.Except(GetDryRunAllowedBlockingReasons()).Any();
        bool canGenerateUsbSummary = !usbReasons.Except(
            [.. GetDryRunAllowedBlockingReasons(), MediaPreflightBlockingReason.Arm64RequiresGpt]).Any();

        return new MediaPreflightEvaluation
        {
            CanGenerateIsoSummary = canGenerateIsoSummary,
            CanGenerateUsbSummary = canGenerateUsbSummary,
            CanCreateIso = canGenerateIsoSummary && options.IsFinalExecutionEnabled && !isoReasons.Any(),
            CanCreateUsb = canGenerateUsbSummary && options.IsFinalExecutionEnabled && !usbReasons.Any(),
            CanCreatePxe = options.IsFinalExecutionEnabled && !pxeReasons.Any(),
            CanCreateUsbWithoutCache = options.IsFinalExecutionEnabled && !usbWithoutCacheReasons.Any(),
            EffectiveUsbPartitionStyle = effectiveUsbPartitionStyle,
            IsoBlockingReasons = isoReasons,
            UsbBlockingReasons = usbReasons,
            PxeBlockingReasons = pxeReasons,
            UsbWithoutCacheBlockingReasons = usbWithoutCacheReasons
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

        // Unlike languages, an empty list still blocks: a layout that cannot be resolved would silently build the default.
        if (!string.IsNullOrWhiteSpace(options.WinPeKeyboardLayoutId)
            && !options.AvailableKeyboardLayoutIds.Contains(options.WinPeKeyboardLayoutId, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add(MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable);
        }

        if (HasCustomImageArchitectureMismatch(options.CustomImages, options.Architecture))
        {
            reasons.Add(MediaPreflightBlockingReason.CustomImageArchitectureMismatch);
        }

        if (!options.IsFinalExecutionEnabled)
        {
            reasons.Add(MediaPreflightBlockingReason.FinalExecutionDeferred);
        }

        return reasons;
    }

    /// <summary>
    /// Determines whether media built for <paramref name="architecture"/> would embed a custom image index built for another architecture.
    /// </summary>
    /// <remarks>
    /// Only enabled settings and included images are checked because only those images are copied to the media, and every
    /// index of an included image stays selectable at deployment time. An x86 index never matches supported media.
    /// An index whose architecture is "unknown" or empty does not block: DISM reports "unknown" for unrecognized processor
    /// architectures and import accepts such images, so preflight cannot prove a mismatch; deployment must still reject
    /// an image it cannot apply.
    /// </remarks>
    /// <param name="settings">The custom image settings that will be published to the media.</param>
    /// <param name="architecture">The selected media architecture.</param>
    /// <returns><see langword="true"/> when an included index reports a known, different architecture.</returns>
    public static bool HasCustomImageArchitectureMismatch(CustomImagesSettings settings, WinPeArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return false;
        }

        string mediaArchitecture = architecture == WinPeArchitecture.Arm64 ? "arm64" : "x64";
        return settings.Images
            .Where(image => image.IsIncluded)
            .SelectMany(image => image.Indexes)
            .Any(index => IsOtherKnownArchitecture(index.Architecture, mediaArchitecture));
    }

    private static bool IsOtherKnownArchitecture(string? imageArchitecture, string mediaArchitecture)
    {
        return !string.IsNullOrWhiteSpace(imageArchitecture)
            && !string.Equals(imageArchitecture, "unknown", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(imageArchitecture, mediaArchitecture, StringComparison.OrdinalIgnoreCase);
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

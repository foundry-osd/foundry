// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Declares every status that WinPE media services report through progress callbacks. The values are stable
/// identifiers that consumers map to localized text; their English wording is also the diagnostic log text.
/// </summary>
/// <remarks>
/// Statuses emitted by PowerShell assets must match one of these values exactly, because the script output is
/// forwarded verbatim.
/// </remarks>
public static class WinPeProgressStatuses
{
    public const string ResolvingWindowsSourceCatalog = "Resolving Windows source catalog.";
    public const string SelectedWindowsSourcePackage = "Selected Windows source package.";
    public const string PreparingWindowsSourcePackage = "Preparing Windows source package.";
    public const string ResolvingWindowsImageIndex = "Resolving Windows image index.";
    public const string ExportingWindowsSourceImage = "Exporting Windows image for boot image preparation.";
    public const string MountingWindowsSourceImage = "Mounting Windows source image.";
    public const string StagingBootImageDependencies = "Staging boot image dependencies.";
    public const string ReplacingBootImageWithWinRe = "Replacing boot image with WinRE.";
    public const string BootImageDependenciesReady = "Boot image dependencies are ready.";

    public const string PreparingBootImageCustomization = "Preparing boot image customization.";
    public const string MountingBootImage = "Mounting boot image.";
    public const string InjectingDrivers = "Injecting drivers into mounted image.";
    public const string ApplyingLanguageAndComponents = "Applying language and optional components.";
    public const string ProvisioningRuntimePayloads = "Provisioning Foundry runtime payloads.";
    public const string ProvisioningBootAssets = "Provisioning Foundry boot assets.";
    public const string CommittingImageChanges = "Committing image changes.";
    public const string ImageCustomizationCompleted = "Image customization completed.";

    public const string DismExportingWindowsImage = "Exporting Windows image with DISM.";
    public const string DismMountingImage = "Mounting image with DISM.";
    public const string DismInjectingDrivers = "Injecting drivers with DISM.";
    public const string DismApplyingLanguagePack = "Applying language pack with DISM.";
    public const string DismApplyingOptionalComponents = "Applying optional components with DISM.";
    public const string DismApplyingInternationalSettings = "Applying international settings with DISM.";
    public const string DismCommittingImageChanges = "Committing image changes with DISM.";
    public const string DismDiscardingMountedImage = "Discarding mounted image with DISM.";

    public const string DownloadingWindowsSourcePackage = "Downloading Windows source package.";
    public const string VerifyingCachedSource = "Verifying cached source.";
    public const string ReusingWindowsSourcePackage = "Reusing verified Windows source package.";
    public const string WindowsSourcePackageReady = "Windows source package downloaded and verified.";
    public const string DownloadingDriverPackage = "Downloading driver package.";
    public const string ReusingDriverPackage = "Reusing cached driver package.";
    public const string DriverPackageReady = "Driver package downloaded and verified.";
    public const string DownloadingRuntimePayload = "Downloading runtime payload.";

    public const string PreparingIsoOutput = "Preparing ISO output path.";
    public const string PreparingIsoWorkspace = "Preparing ISO workspace.";
    public const string CreatingIsoMedia = "Creating ISO media.";
    public const string RunningMakeWinPeMediaIso = "Running MakeWinPEMedia for ISO.";
    public const string FinalizingIsoOutput = "Finalizing ISO output.";
    public const string IsoMediaCompleted = "ISO media completed.";

    public const string StagingCustomImages = "Staging custom Windows images.";
    public const string CustomImagesVerified = "Custom Windows images verified.";
    public const string StagingPostInstallation = "Staging post-installation content.";
    public const string PostInstallationVerified = "Post-installation content verified.";
    public const string StagingCustomDrivers = "Staging custom driver sets.";
    public const string CustomDriversVerified = "Custom driver sets verified.";

    public const string ValidatingUsbTarget = "Validating USB target.";
    public const string CheckingUsbTargetSafety = "Checking USB target safety.";
    public const string InspectingUsbMediaLayout = "Inspecting USB media layout.";
    public const string PartitioningUsbTarget = "Partitioning and formatting USB target.";
    public const string OpeningUsbDisk = "Opening USB disk.";
    public const string PreparingUsbDiskAttributes = "Preparing USB disk attributes.";
    public const string ClearingUsbPartitionTable = "Clearing USB partition table.";
    public const string InitializingUsbPartitionTable = "Initializing USB partition table.";
    public const string CreatingUsbBootPartition = "Creating BOOT partition.";
    public const string FormattingUsbBootPartition = "Formatting BOOT partition.";
    public const string CreatingUsbCachePartition = "Creating cache partition.";
    public const string FormattingUsbCachePartition = "Formatting cache partition.";
    public const string UsbPartitionsFormatted = "USB partitions formatted.";
    public const string CopyingWinPeMediaToUsb = "Copying WinPE media to USB.";
    public const string VerifyingUsbBootMedia = "Verifying USB boot media.";
    public const string PreparingUsbCachePartition = "Preparing USB cache partition.";
    public const string ProvisioningUsbRuntimePayloads = "Provisioning USB runtime payloads.";
    public const string UsbMediaCompleted = "USB media completed.";
    public const string UsbBootPartitionUpdated = "USB boot partition updated.";

    /// <summary>
    /// Gets every declared status.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = typeof(WinPeProgressStatuses)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);
}

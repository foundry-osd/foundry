// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeUsbMediaService
{
    /// <summary>
    /// Writes the content of an ISO to one FAT32 partition: the boot files, <c>sources\boot.wim</c> with the runtimes
    /// embedded, then the imported content at the paths the ISO uses. Every check that can fail runs before the disk is erased.
    /// </summary>
    private async Task<WinPeResult<WinPeUsbProvisionResult>> ProvisionSingleVolumeAsync(UsbOutputOptions options,
        DiskIdentity expectedIdentity, WinPeBuildArtifact artifact, WinPeToolPaths tools, bool useBootEx, CancellationToken cancellationToken)
    {
        if (useBootEx)
        {
            WinPeResult bootConfiguration = ConfigureBootFiles(artifact.MediaDirectoryPath, artifact);
            if (!bootConfiguration.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(bootConfiguration.Error!);
        }

        WinPeResult mediaVerification = VerifyBootArtifacts(artifact.MediaDirectoryPath, artifact.Architecture);
        if (!mediaVerification.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(mediaVerification.Error!);

        WinPeResult contentValidation = await ValidateSingleVolumeContentAsync(options, artifact, cancellationToken).ConfigureAwait(false);
        if (!contentValidation.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(contentValidation.Error!);

        ReportProgress(options.Progress, 20, WinPeProgressStatuses.PartitioningUsbTarget);
        cancellationToken.ThrowIfCancellationRequested();
        // A started disk mutation must finish before cancellation can stop the next stage.
        WinPeResult<WinPeUsbProvisionResult> provisioningResult = await ProvisionDiskAsync(expectedIdentity, options.PartitionStyle,
            options.FormatMode, UsbMediaLayout.WithoutCache, tools, artifact.WorkingDirectoryPath, options.Progress,
            CancellationToken.None).ConfigureAwait(false);
        if (!provisioningResult.IsSuccess) return provisioningResult;
        cancellationToken.ThrowIfCancellationRequested();

        WinPeUsbProvisionResult provisionedUsb = provisioningResult.Value!;
        string mediaRootPath = $"{provisionedUsb.BootDriveLetter}\\";
        ReportProgress(options.Progress, 56, WinPeProgressStatuses.CopyingWinPeMediaToUsb);
        WinPeResult copyResult = await CopyMediaAsync(artifact.MediaDirectoryPath, mediaRootPath, artifact.WorkingDirectoryPath,
            CancellationToken.None).ConfigureAwait(false);
        if (!copyResult.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(copyResult.Error!);
        cancellationToken.ThrowIfCancellationRequested();

        WinPeResult publication = await PublishCustomImagesAsync(options, artifact, tools, mediaRootPath,
            SingleVolumeContentProgressRange, updatesExistingMedia: false, cancellationToken).ConfigureAwait(false);
        if (!publication.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(publication.Error!);

        ReportProgress(options.Progress, 92, WinPeProgressStatuses.VerifyingUsbBootMedia);
        WinPeResult verificationResult = VerifyBootArtifacts(mediaRootPath, artifact.Architecture);
        if (!verificationResult.IsSuccess) return WinPeResult<WinPeUsbProvisionResult>.Failure(verificationResult.Error!);

        ReportProgress(options.Progress, 100, WinPeProgressStatuses.UsbMediaCompleted);
        return WinPeResult<WinPeUsbProvisionResult>.Success(provisionedUsb);
    }

    /// <summary>
    /// Checks the bindings and sources of the imported content, then the FAT32 file size limit and the partition capacity.
    /// </summary>
    private async Task<WinPeResult> ValidateSingleVolumeContentAsync(UsbOutputOptions options, WinPeBuildArtifact artifact,
        CancellationToken cancellationToken)
    {
        var externalFileLengths = new List<long>();
        if (HasExternalContent(options))
        {
            ReportProgress(options.Progress, 10, WinPeProgressStatuses.CheckingMediaContent);
            try
            {
                if (options.CustomImages is not null)
                    WinPeCustomImageMediaService.ValidateConfigurationBinding(options.CustomImages, options.DeployConfigurationJson ?? string.Empty);
                if (options.PostInstallation is not null)
                    WinPePreOobeMediaService.ValidateConfigurationBinding(options.PostInstallation, options.DeployConfigurationJson ?? string.Empty);
                if (options.CustomDrivers is not null)
                    WinPeCustomDriverSetMediaService.ValidateConfigurationBinding(options.CustomDrivers, options.DeployConfigurationJson ?? string.Empty);
                var sourcePaths = new List<string> { artifact.WorkingDirectoryPath };
                if (options.CustomImages is not null)
                {
                    sourcePaths.AddRange(options.CustomImages.Files.Select(file => file.SourcePath));
                    externalFileLengths.AddRange(options.CustomImages.Files.Select(file => file.Length));
                    externalFileLengths.Add(options.CustomImages.ManifestBytes.LongLength);
                }
                foreach (WinPeContentPackageMediaLease package in new[] { options.PostInstallation, options.CustomDrivers }.OfType<WinPeContentPackageMediaLease>())
                {
                    sourcePaths.AddRange(package.Files.Select(file => file.SourcePath));
                    externalFileLengths.AddRange(package.Files.Select(file => file.Length));
                    externalFileLengths.Add(package.ManifestBytes.LongLength);
                }
                await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
                if (options.CustomImages is not null)
                    await _customImagePublisher.ValidateSourcesAsync(options.CustomImages, cancellationToken).ConfigureAwait(false);
                if (options.PostInstallation is not null)
                    await _postInstallationPublisher.ValidateSourcesAsync(options.PostInstallation, cancellationToken).ConfigureAwait(false);
                if (options.CustomDrivers is not null)
                    await _customDriversPublisher.ValidateSourcesAsync(options.CustomDrivers, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or OverflowException)
            {
                return ExternalContentFailure(options, exception, updatesExistingMedia: false);
            }
        }

        return WinPeUsbSingleVolumePolicy.Validate(artifact.MediaDirectoryPath, externalFileLengths, options.ExpectedDiskSizeBytes, cancellationToken);
    }

    private static readonly (int Start, int End) SingleVolumeContentProgressRange = (60, 90);
}

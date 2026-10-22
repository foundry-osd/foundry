// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeUsbMediaService
{
    private async Task<WinPeResult> PrepareCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact, WinPeToolPaths tools,
        string? existingDataRoot, CancellationToken cancellationToken)
    {
        if (!HasExternalContent(options)) return WinPeResult.Success();
        try
        {
            if (options.CustomImages is not null)
                WinPeCustomImageMediaService.ValidateConfigurationBinding(options.CustomImages, options.DeployConfigurationJson ?? string.Empty);
            if (options.PostInstallation is not null)
                WinPePreOobeMediaService.ValidateConfigurationBinding(options.PostInstallation, options.DeployConfigurationJson ?? string.Empty);
            if (options.CustomDrivers is not null)
                WinPeCustomDriverSetMediaService.ValidateConfigurationBinding(options.CustomDrivers, options.DeployConfigurationJson ?? string.Empty);
            var sourcePaths = new List<string> { artifact.WorkingDirectoryPath };
            if (options.CustomImages is not null) sourcePaths.AddRange(options.CustomImages.Files.Select(file => file.SourcePath));
            if (options.PostInstallation is not null) sourcePaths.AddRange(options.PostInstallation.Files.Select(file => file.SourcePath));
            if (options.CustomDrivers is not null) sourcePaths.AddRange(options.CustomDrivers.Files.Select(file => file.SourcePath));
            long runtimeBytes = 0;
            if (options.RuntimePayloadProvisioning is { } runtime)
            {
                foreach (WinPeRuntimePayloadApplicationOptions application in new[] { runtime.Bootstrap, runtime.Connect, runtime.Deploy, runtime.PostInstall })
                {
                    if (!application.IsEnabled) continue;
                    if (string.IsNullOrWhiteSpace(application.ArchivePath))
                        throw new InvalidDataException("Runtime payloads must be prepared before custom-image USB creation.");
                    sourcePaths.Add(application.ArchivePath);
                }
                // Bootstrap is already embedded in boot.wim; the other runtimes use data-volume space.
                runtimeBytes = new[] { runtime.Connect, runtime.Deploy, runtime.PostInstall }
                    .Where(application => application.IsEnabled)
                    .Sum(application => new FileInfo(application.ArchivePath).Length);
            }
            await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            if (options.CustomImages is not null)
                await _customImagePublisher.ValidateSourcesAsync(options.CustomImages, cancellationToken).ConfigureAwait(false);
            if (options.PostInstallation is not null)
                await _postInstallationPublisher.ValidateSourcesAsync(options.PostInstallation, cancellationToken).ConfigureAwait(false);
            if (options.CustomDrivers is not null)
                await _customDriversPublisher.ValidateSourcesAsync(options.CustomDrivers, cancellationToken).ConfigureAwait(false);
            if (existingDataRoot is null)
            {
                // Existing files do not count as reclaimable: a new disk is wholly formatted, while updates
                // preserve old generations and reserve their replacement space before BOOT is changed.
                ulong required = checked(WinPeUsbCapacityPolicy.NewBootPartitionSizeBytes +
                    (ulong)(options.CustomImages?.TotalBytes ?? 0) + (ulong)(options.PostInstallation?.TotalBytes ?? 0) +
                    (ulong)(options.CustomDrivers?.TotalBytes ?? 0) +
                    (ulong)runtimeBytes + 2UL * (ulong)WinPeCustomImageMediaService.DataReserveBytes);
                if (options.ExpectedDiskSizeBytes < required)
                    throw new IOException("The USB disk has insufficient capacity for BOOT, custom images, post-installation content, driver sets and runtime payloads.");
                await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // One combined check: the first present content validates the volume with the missing bytes of the content after it.
                long driversBytes = options.CustomDrivers is null ? 0 : await _customDriversPublisher
                    .GetRequiredBytesAsync(options.CustomDrivers, existingDataRoot, cancellationToken).ConfigureAwait(false);
                if (options.CustomImages is not null)
                {
                    long postInstallationBytes = options.PostInstallation is null ? 0 : await _postInstallationPublisher
                        .GetRequiredBytesAsync(options.PostInstallation, existingDataRoot, cancellationToken).ConfigureAwait(false);
                    await _customImagePublisher.ValidateCapacityAsync(options.CustomImages, existingDataRoot,
                        checked(runtimeBytes + postInstallationBytes + driversBytes), cancellationToken).ConfigureAwait(false);
                }
                else if (options.PostInstallation is not null)
                    await _postInstallationPublisher.ValidateCapacityAsync(options.PostInstallation, existingDataRoot,
                        checked(runtimeBytes + driversBytes), cancellationToken).ConfigureAwait(false);
                else
                    await _customDriversPublisher.ValidateCapacityAsync(options.CustomDrivers!, existingDataRoot, runtimeBytes, cancellationToken).ConfigureAwait(false);
                WinPeResult publication = await PublishCustomImagesAsync(options, artifact, tools, existingDataRoot,
                    UpdateContentProgressRange, cancellationToken).ConfigureAwait(false);
                if (!publication.IsSuccess) return publication;
                // Recheck provenance after a potentially long copy and immediately before BOOT mutation.
                await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            }
            return WinPeResult.Success();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or OverflowException)
        {
            return CustomImageFailure(exception);
        }
    }

    /// <summary>
    /// Publishes custom images, then post-installation content, then driver sets, each inside its own share of
    /// <paramref name="progressRange"/> so the copy cannot restart the stage percentage.
    /// </summary>
    private async Task<WinPeResult> PublishCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact,
        WinPeToolPaths tools, string dataRoot, (int Start, int End) progressRange, CancellationToken cancellationToken)
    {
        if (!HasExternalContent(options)) return WinPeResult.Success();
        try
        {
            WinPeResult<WinPeUsbDiskIdentity> identity = await GetDiskIdentityAsync(GetExpectedIdentity(options), tools,
                artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            if (!identity.IsSuccess) return WinPeResult.Failure(identity.Error!);
            await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            (int imagesEnd, int postInstallationEnd) = WinPeScaledProgress.SplitExternalContent(progressRange.Start, progressRange.End,
                options.CustomImages is not null, options.PostInstallation is not null, options.CustomDrivers is not null);
            if (options.CustomImages is not null)
                await _customImagePublisher.PublishAsync(options.CustomImages, dataRoot, cancellationToken,
                    WinPeScaledProgress.Create(options.Progress, progressRange.Start, imagesEnd)).ConfigureAwait(false);
            if (options.PostInstallation is not null)
            {
                await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
                await _postInstallationPublisher.PublishAsync(options.PostInstallation, dataRoot, cancellationToken,
                    WinPeScaledProgress.Create(options.Progress, imagesEnd, postInstallationEnd)).ConfigureAwait(false);
            }
            if (options.CustomDrivers is not null)
            {
                await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
                await _customDriversPublisher.PublishAsync(options.CustomDrivers, dataRoot, cancellationToken,
                    WinPeScaledProgress.Create(options.Progress, postInstallationEnd, progressRange.End)).ConfigureAwait(false);
            }
            return WinPeResult.Success();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return CustomImageFailure(exception);
        }
    }

    private static bool HasExternalContent(UsbOutputOptions options) =>
        options.CustomImages is not null || options.PostInstallation is not null || options.CustomDrivers is not null;

    private static WinPeResult CustomImageFailure(Exception exception) => WinPeResult.Failure(
        WinPeErrorCodes.CustomImageMediaFailed, "Custom Windows image media preparation failed.", exception.Message,
        stage: "Prepare custom Windows images", exception: exception);
}

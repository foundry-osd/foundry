// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeUsbMediaService
{
    private async Task<WinPeResult> PrepareCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact, WinPeToolPaths tools,
        string? existingDataRoot, CancellationToken cancellationToken)
    {
        if (options.CustomImages is null && options.PostInstallation is null) return WinPeResult.Success();
        try
        {
            if (options.CustomImages is not null)
                WinPeCustomImageMediaService.ValidateConfigurationBinding(options.CustomImages, options.DeployConfigurationJson ?? string.Empty);
            if (options.PostInstallation is not null)
                WinPePreOobeMediaService.ValidateConfigurationBinding(options.PostInstallation, options.DeployConfigurationJson ?? string.Empty);
            var sourcePaths = new List<string> { artifact.WorkingDirectoryPath };
            if (options.CustomImages is not null) sourcePaths.AddRange(options.CustomImages.Files.Select(file => file.SourcePath));
            if (options.PostInstallation is not null) sourcePaths.AddRange(options.PostInstallation.Files.Select(file => file.SourcePath));
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
            if (existingDataRoot is null)
            {
                // Existing files do not count as reclaimable: a new disk is wholly formatted, while updates
                // preserve old generations and reserve their replacement space before BOOT is changed.
                ulong required = checked(WinPeUsbCapacityPolicy.NewBootPartitionSizeBytes +
                    (ulong)(options.CustomImages?.TotalBytes ?? 0) + (ulong)(options.PostInstallation?.TotalBytes ?? 0) +
                    (ulong)runtimeBytes + 2UL * (ulong)WinPeCustomImageMediaService.DataReserveBytes);
                if (options.ExpectedDiskSizeBytes < required)
                    throw new IOException("The USB disk has insufficient capacity for BOOT, custom images and runtime payloads.");
                await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (options.CustomImages is not null)
                {
                    long postInstallationBytes = options.PostInstallation is null ? 0 : await _postInstallationPublisher
                        .GetRequiredBytesAsync(options.PostInstallation, existingDataRoot, cancellationToken).ConfigureAwait(false);
                    await _customImagePublisher.ValidateCapacityAsync(options.CustomImages, existingDataRoot,
                        checked(runtimeBytes + postInstallationBytes), cancellationToken).ConfigureAwait(false);
                }
                else if (options.PostInstallation is not null)
                    await _postInstallationPublisher.ValidateCapacityAsync(options.PostInstallation, existingDataRoot, runtimeBytes, cancellationToken).ConfigureAwait(false);
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
    /// Publishes custom images, then post-installation content, each inside its own share of
    /// <paramref name="progressRange"/> so the copy cannot restart the stage percentage.
    /// </summary>
    private async Task<WinPeResult> PublishCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact,
        WinPeToolPaths tools, string dataRoot, (int Start, int End) progressRange, CancellationToken cancellationToken)
    {
        if (options.CustomImages is null && options.PostInstallation is null) return WinPeResult.Success();
        try
        {
            WinPeResult<WinPeUsbDiskIdentity> identity = await GetDiskIdentityAsync(GetExpectedIdentity(options), tools,
                artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            if (!identity.IsSuccess) return WinPeResult.Failure(identity.Error!);
            await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            // Custom images are usually far larger than post-installation content, so they get most of the range.
            int split = options.CustomImages is null ? progressRange.Start
                : options.PostInstallation is null ? progressRange.End
                : progressRange.Start + ((progressRange.End - progressRange.Start) * 4 / 5);
            if (options.CustomImages is not null)
                await _customImagePublisher.PublishAsync(options.CustomImages, dataRoot, cancellationToken,
                    WinPeScaledProgress.Create(options.Progress, progressRange.Start, split)).ConfigureAwait(false);
            if (options.PostInstallation is not null)
            {
                await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
                await _postInstallationPublisher.PublishAsync(options.PostInstallation, dataRoot, cancellationToken,
                    WinPeScaledProgress.Create(options.Progress, split, progressRange.End)).ConfigureAwait(false);
            }
            return WinPeResult.Success();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return CustomImageFailure(exception);
        }
    }

    private static WinPeResult CustomImageFailure(Exception exception) => WinPeResult.Failure(
        WinPeErrorCodes.CustomImageMediaFailed, "Custom Windows image media preparation failed.", exception.Message,
        stage: "Prepare custom Windows images", exception: exception);
}

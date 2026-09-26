// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeUsbMediaService
{
    private async Task<WinPeResult> PrepareCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact, WinPeToolPaths tools,
        string? existingDataRoot, CancellationToken cancellationToken)
    {
        if (options.CustomImages is null) return WinPeResult.Success();
        try
        {
            WinPeCustomImageMediaService.ValidateConfigurationBinding(options.CustomImages,
                options.DeployConfigurationJson ?? string.Empty);
            var sourcePaths = new List<string> { artifact.WorkingDirectoryPath };
            sourcePaths.AddRange(options.CustomImages.Files.Select(file => file.SourcePath));
            long runtimeBytes = 0;
            if (options.RuntimePayloadProvisioning is { } runtime)
            {
                foreach (WinPeRuntimePayloadApplicationOptions application in new[] { runtime.Bootstrap, runtime.Connect, runtime.Deploy })
                {
                    if (!application.IsEnabled) continue;
                    if (string.IsNullOrWhiteSpace(application.ArchivePath))
                        throw new InvalidDataException("Runtime payloads must be prepared before custom-image USB creation.");
                    sourcePaths.Add(application.ArchivePath);
                }
                // Bootstrap is already embedded in boot.wim; only Connect and Deploy use data-volume space.
                runtimeBytes = new[] { runtime.Connect, runtime.Deploy }
                    .Where(application => application.IsEnabled)
                    .Sum(application => new FileInfo(application.ArchivePath).Length);
            }
            await _customImagePublisher.ValidateInputDisksAsync(sourcePaths, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            await _customImagePublisher.ValidateSourcesAsync(options.CustomImages, cancellationToken).ConfigureAwait(false);
            if (existingDataRoot is null)
            {
                // Existing files do not count as reclaimable: a new disk is wholly formatted, while updates
                // preserve old generations and reserve their replacement space before BOOT is changed.
                ulong required = checked(WinPeUsbCapacityPolicy.NewBootPartitionSizeBytes +
                    (ulong)options.CustomImages.TotalBytes + (ulong)runtimeBytes + 2UL * (ulong)WinPeCustomImageMediaService.DataReserveBytes);
                if (options.ExpectedDiskSizeBytes < required)
                    throw new IOException("The USB disk has insufficient capacity for BOOT, custom images and runtime payloads.");
            }
            else
            {
                await _customImagePublisher.ValidateCapacityAsync(options.CustomImages, existingDataRoot, runtimeBytes, cancellationToken).ConfigureAwait(false);
                WinPeResult publication = await PublishCustomImagesAsync(options, artifact, tools, existingDataRoot, cancellationToken).ConfigureAwait(false);
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

    private async Task<WinPeResult> PublishCustomImagesAsync(UsbOutputOptions options, WinPeBuildArtifact artifact,
        WinPeToolPaths tools, string dataRoot, CancellationToken cancellationToken)
    {
        if (options.CustomImages is null) return WinPeResult.Success();
        try
        {
            WinPeResult<WinPeUsbDiskIdentity> identity = await GetDiskIdentityAsync(GetExpectedIdentity(options), tools,
                artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
            if (!identity.IsSuccess) return WinPeResult.Failure(identity.Error!);
            await _customImagePublisher.ValidateDestinationDiskAsync(dataRoot, options.TargetDiskNumber!.Value, cancellationToken).ConfigureAwait(false);
            await _customImagePublisher.PublishAsync(options.CustomImages, dataRoot, cancellationToken, options.Progress).ConfigureAwait(false);
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

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogFirmwareServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractAsync_WhenCancelledDuringExtraction_WaitsForExitAndPreservesFailure(bool extractionFails)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-firmware-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var extractor = new HeldExtraction(extractionFails);
            var service = new MicrosoftUpdateCatalogFirmwareService(extractor, new FirmwareCatalog(), new FirmwareDownloader(),
                NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

            string rawDirectory = Path.Combine(root, "Raw");
            Directory.CreateDirectory(rawDirectory);
            await File.WriteAllTextAsync(Path.Combine(rawDirectory, "firmware.cab"), "cab", TestContext.Current.CancellationToken);
            Task<int> run = service.ExtractAsync(rawDirectory, Path.Combine(root, "Extracted"), cancellation.Token);
            await extractor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();
            bool wasPending = !run.IsCompleted;
            bool extractionWasCancellable = extractor.ReceivedToken.CanBeCanceled;
            extractor.Release.SetResult();
            Exception? failure = await Record.ExceptionAsync(() => run);

            Assert.True(wasPending);
            Assert.False(extractionWasCancellable);
            if (extractionFails)
            {
                Assert.IsType<IOException>(failure);
                Assert.Equal("Extraction failed.", failure.Message);
            }
            else
            {
                Assert.IsAssignableFrom<OperationCanceledException>(failure);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class HeldExtraction(bool fail) : IArchiveExtractionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReceivedToken { get; private set; }

        public async Task ExtractWithSevenZipAsync(string archivePath, string extractedPath, string workingDirectory,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            ReceivedToken = cancellationToken;
            Started.SetResult();
            await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (fail)
            {
                throw new IOException("Extraction failed.");
            }

            await File.WriteAllTextAsync(Path.Combine(extractedPath, "firmware.inf"), "firmware", TestContext.Current.CancellationToken);
        }
    }

    private sealed class FirmwareCatalog : IMicrosoftUpdateCatalogClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Extraction must not query the catalog.");
        public Task<IReadOnlyList<MicrosoftUpdateCatalogUpdate>> SearchAsync(string searchQuery, bool descending = true,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Extraction must not query the catalog.");
        public Task<IReadOnlyList<MicrosoftUpdateCatalogDownload>> GetDownloadsAsync(string updateId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Extraction must not query the catalog.");
    }

    private sealed class FirmwareDownloader : IArtifactDownloadService
    {
        public Task<ArtifactDownloadResult> DownloadAsync(string sourceUrl, string destinationPath, string? expectedHash = null,
            long? expectedSizeBytes = null, string? artifactKind = null, CancellationToken cancellationToken = default,
            IProgress<DownloadProgress>? progress = null) =>
            throw new InvalidOperationException("Extraction must not download a payload.");
    }
}

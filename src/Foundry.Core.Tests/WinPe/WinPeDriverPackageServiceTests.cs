// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDriverPackageServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenCancelledDuringExtraction_WaitsForStageAndPreservesFailures(bool fails)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-cancel-{Guid.NewGuid():N}");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StaticPackageHandler([42]));
        var runner = new FakeExtractionRunner(onRun: token =>
        {
            Assert.False(token.CanBeCanceled);
            caller.Cancel();
        }, exitCode: fails ? 5 : 0);
        var service = new WinPeDriverPackageService(runner, client, "7za.exe");
        try
        {
            Task<WinPeResult<WinPePreparedDriverSet>> operation = service.PrepareAsync(
                [CreatePackage()], Path.Combine(root, "downloads"), Path.Combine(root, "extracted"), null, caller.Token);
            if (fails)
            {
                WinPeResult<WinPePreparedDriverSet> result = await operation;
                Assert.False(result.IsSuccess);
                Assert.Equal(WinPeErrorCodes.DriverExtractionFailed, result.Error?.Code);
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_DownloadsValidatesAndExtractsPackage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-package-{Guid.NewGuid():N}");
        string downloadRoot = Path.Combine(root, "downloads");
        string extractRoot = Path.Combine(root, "extracted");
        byte[] packageBytes = Encoding.UTF8.GetBytes("driver package");
        string packageHash = Convert.ToHexString(SHA256.HashData(packageBytes));
        var runner = new FakeExtractionRunner();
        var service = new WinPeDriverPackageService(
            runner,
            new HttpClient(new StaticPackageHandler(packageBytes)),
            "7za.exe");

        try
        {
            WinPeResult<WinPePreparedDriverSet> result = await service.PrepareAsync(
                [
                    new WinPeDriverCatalogEntry
                    {
                        Id = "dell-package",
                        Vendor = WinPeVendorSelection.Dell,
                        DownloadUri = "https://example.test/dell.cab",
                        FileName = "dell.cab",
                        Format = "cab",
                        Sha256 = packageHash
                    }
                ],
                downloadRoot,
                extractRoot,
                null,
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.True(File.Exists(Path.Combine(downloadRoot, "dell.cab")));
            string extractionDirectory = Assert.Single(result.Value!.ExtractionDirectories);
            Assert.True(File.Exists(Path.Combine(extractionDirectory, "driver.inf")));
            Assert.Contains(runner.Executions, execution => execution.FileName == "7za.exe");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenExecutableExtractionHasNoInf_ReturnsExtractionFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-package-{Guid.NewGuid():N}");
        byte[] packageBytes = Encoding.UTF8.GetBytes("driver package");
        var service = new WinPeDriverPackageService(
            new FakeExtractionRunner(createInf: false),
            new HttpClient(new StaticPackageHandler(packageBytes)),
            "7za.exe");

        try
        {
            WinPeResult<WinPePreparedDriverSet> result = await service.PrepareAsync(
                [
                    new WinPeDriverCatalogEntry
                    {
                        Id = "setup",
                        DownloadUri = "https://example.test/setup.exe",
                        FileName = "setup.exe",
                        Format = "exe"
                    }
                ],
                Path.Combine(root, "downloads"),
                Path.Combine(root, "extracted"),
                null,
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.DriverExtractionFailed, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenDownloadReturnsError_ClassifiesHttpStatus()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-package-{Guid.NewGuid():N}");
        var service = new WinPeDriverPackageService(
            new FakeExtractionRunner(),
            new HttpClient(new StaticPackageHandler([], HttpStatusCode.BadGateway)),
            "7za.exe");

        try
        {
            WinPeResult<WinPePreparedDriverSet> result = await service.PrepareAsync(
                [CreatePackage()],
                Path.Combine(root, "downloads"),
                Path.Combine(root, "extracted"),
                null,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeFailureReasons.HttpStatus, result.Error?.FailureReason);
            Assert.Contains("HTTP status: 502 Bad Gateway", result.Error?.Details, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenHttpClientTimesOut_ClassifiesTimeoutAndPreservesCause()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-package-{Guid.NewGuid():N}");
        var timeout = new TaskCanceledException("The request timed out.");
        var service = new WinPeDriverPackageService(
            new FakeExtractionRunner(),
            new HttpClient(new StaticPackageHandler([], exception: timeout)),
            "7za.exe");

        try
        {
            WinPeResult<WinPePreparedDriverSet> result = await service.PrepareAsync(
                [CreatePackage()],
                Path.Combine(root, "downloads"),
                Path.Combine(root, "extracted"),
                null,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeFailureReasons.Timeout, result.Error?.FailureReason);
            TimeoutException failure = Assert.IsType<TimeoutException>(result.Error?.Exception);
            Assert.Same(timeout, failure.InnerException);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static WinPeDriverCatalogEntry CreatePackage() => new()
    {
        Id = "package",
        DownloadUri = "https://example.test/package.cab",
        FileName = "package.cab",
        Format = "cab"
    };

    private sealed class StaticPackageHandler(
        byte[] content,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Exception? exception = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                return Task.FromException<HttpResponseMessage>(exception);
            }

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class FakeExtractionRunner(bool createInf = true, Action<CancellationToken>? onRun = null, int exitCode = 0) : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            onRun?.Invoke(cancellationToken);
            Executions.Add(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            });

            if (createInf)
            {
                Directory.CreateDirectory(workingDirectory);
                File.WriteAllText(Path.Combine(workingDirectory, "driver.inf"), string.Empty);
            }

            return Task.FromResult(new WinPeProcessExecution
            {
                ExitCode = exitCode,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}

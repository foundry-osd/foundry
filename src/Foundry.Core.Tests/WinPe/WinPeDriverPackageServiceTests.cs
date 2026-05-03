using Foundry.Core.Services.WinPe;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDriverPackageServiceTests
{
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
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.DriverExtractionFailed, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StaticPackageHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class FakeExtractionRunner(bool createInf = true) : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
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

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPePowerShell7ProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_ExtractsPowerShellAndConfiguresEnvironment()
    {
        using TempImage image = TempImage.Create();
        byte[] zip = CreateZip("pwsh.dat");
        var handler = new RoutingHandler { { "https://example/ps7.zip", zip } };
        var runner = new RecordingProcessRunner();

        var service = new WinPePowerShell7ProvisioningService(new HttpClient(handler), runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPePowerShell7ProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                Release = new PowerShell7Release
                {
                    Version = "7.5.8",
                    Tag = "v7.5.8",
                    AssetName = "PowerShell-7.5.8-win-x64.zip",
                    DownloadUrl = "https://example/ps7.zip"
                },
                CacheDirectoryPath = image.CachePath,
                WorkingDirectoryPath = image.WorkPath
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(image.MountedImagePath, "Program Files", "PowerShell", "7", "pwsh.dat")));

        Assert.Contains(runner.Arguments, arg => arg.StartsWith("LOAD ", StringComparison.Ordinal));
        Assert.Contains(runner.Arguments, arg => arg.StartsWith("UNLOAD ", StringComparison.Ordinal));
        Assert.Contains(runner.Arguments, arg => arg.Contains("/v Path", StringComparison.Ordinal) && arg.Contains(@"PowerShell\7", StringComparison.Ordinal));
        Assert.Contains(runner.Arguments, arg => arg.Contains("/v PSModulePath", StringComparison.Ordinal) && arg.Contains(@"PowerShell\7\Modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProvisionAsync_WhenSelectedReleaseFails_FallsBackToLatest()
    {
        using TempImage image = TempImage.Create();
        byte[] zip = CreateZip("pwsh.exe");
        var handler = new RoutingHandler { { "https://example/latest.zip", zip } }; // selected URL not registered => 404
        var runner = new RecordingProcessRunner();

        var service = new WinPePowerShell7ProvisioningService(new HttpClient(handler), runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPePowerShell7ProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                Release = new PowerShell7Release
                {
                    Version = "7.4.17",
                    Tag = "v7.4.17",
                    AssetName = "PowerShell-7.4.17-win-x64.zip",
                    DownloadUrl = "https://example/selected.zip"
                },
                FallbackRelease = new PowerShell7Release
                {
                    Version = "7.5.8",
                    Tag = "v7.5.8",
                    AssetName = "PowerShell-7.5.8-win-x64.zip",
                    DownloadUrl = "https://example/latest.zip"
                },
                CacheDirectoryPath = image.CachePath,
                WorkingDirectoryPath = image.WorkPath
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(image.CachePath, "PowerShell-7.5.8-win-x64.zip")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenReleaseMissing_ReturnsValidationFailure()
    {
        using TempImage image = TempImage.Create();
        var service = new WinPePowerShell7ProvisioningService(new HttpClient(new RoutingHandler()), new RecordingProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPePowerShell7ProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                CacheDirectoryPath = image.CachePath,
                WorkingDirectoryPath = image.WorkPath
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private static byte[] CreateZip(string entryName)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using Stream entryStream = entry.Open();
            entryStream.Write("MZ"u8);
        }

        return memory.ToArray();
    }

    private sealed class TempImage : IDisposable
    {
        private TempImage(string rootPath, string mountedImagePath)
        {
            RootPath = rootPath;
            MountedImagePath = mountedImagePath;
            CachePath = Path.Combine(rootPath, "cache");
            WorkPath = Path.Combine(rootPath, "work");
        }

        public string RootPath { get; }
        public string MountedImagePath { get; }
        public string CachePath { get; }
        public string WorkPath { get; }

        public static TempImage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-ps7-{Guid.NewGuid():N}");
            string mount = Path.Combine(root, "mount");
            Directory.CreateDirectory(Path.Combine(mount, "Windows", "System32", "config"));
            File.WriteAllText(Path.Combine(mount, "Windows", "System32", "config", "SYSTEM"), "hive");
            Directory.CreateDirectory(Path.Combine(root, "cache"));
            Directory.CreateDirectory(Path.Combine(root, "work"));
            return new TempImage(root, mount);
        }

        public void Dispose()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    private sealed class RecordingProcessRunner : IWinPeProcessRunner
    {
        public List<string> Arguments { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            Arguments.Add(arguments);
            return Task.FromResult(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                ExitCode = 0
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RoutingHandler : HttpMessageHandler, IEnumerable<KeyValuePair<string, byte[]>>
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string url, byte[] content)
        {
            _responses[url] = content;
        }

        public IEnumerator<KeyValuePair<string, byte[]>> GetEnumerator() => _responses.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _responses.GetEnumerator();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out byte[]? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

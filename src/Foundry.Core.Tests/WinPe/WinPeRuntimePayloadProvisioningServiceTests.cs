using System.IO.Compression;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_WhenLocalArchivesAreProvided_ExtractsToNormalizedIsoAndUsbRuntimeRoots()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe");
        string deployArchivePath = workspace.CreateArchive("deploy.zip", "Foundry.Deploy.exe");
        string legacyConnectSeedPath = Path.Combine(workspace.MountedImagePath, "Foundry", "Seed", "Foundry.Connect.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConnectSeedPath)!);
        File.WriteAllText(legacyConnectSeedPath, "legacy");

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                UsbCacheRootPath = workspace.UsbCacheRootPath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                Deploy = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = deployArchivePath
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy", "win-x64", "Foundry.Deploy.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Deploy", "win-x64", "Foundry.Deploy.exe")));
        Assert.False(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Seed", "Foundry.Connect.zip")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenArchiveAndProjectAreProvided_PrefersArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe");
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.Connect", "Foundry.Connect.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath,
                    ProjectPath = projectPath
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Empty(runner.Executions);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenProjectIsProvided_PublishesSingleFileBeforeProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.Arm64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Deploy = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ProjectPath = projectPath
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeProcessExecution execution = Assert.Single(runner.Executions);
        Assert.Equal("dotnet", execution.FileName);
        Assert.Contains("publish", execution.Arguments);
        Assert.Contains("-r win-arm64", execution.Arguments);
        Assert.Contains("/p:PublishSingleFile=true", execution.Arguments);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy", "win-arm64", "Foundry.Deploy.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenApplicationIsDisabled_DoesNotCreateRuntimeRoot()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                UsbCacheRootPath = workspace.UsbCacheRootPath
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect")));
        Assert.False(Directory.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Deploy")));
    }

    private sealed class FakeRuntimeProcessRunner : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            string outputDirectory = ExtractOutputDirectory(arguments);
            Directory.CreateDirectory(outputDirectory);
            string executableName = arguments.Contains("Foundry.Connect.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.Connect.exe"
                : "Foundry.Deploy.exe";
            File.WriteAllText(Path.Combine(outputDirectory, executableName), executableName);

            var execution = new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            };

            Executions.Add(execution);
            return Task.FromResult(execution);
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

        private static string ExtractOutputDirectory(string arguments)
        {
            int marker = arguments.IndexOf("-o ", StringComparison.Ordinal);
            Assert.True(marker >= 0, arguments);
            string remaining = arguments[(marker + 3)..].Trim();
            if (remaining.StartsWith('"'))
            {
                int endQuote = remaining.IndexOf('"', 1);
                Assert.True(endQuote > 1, remaining);
                return remaining[1..endQuote];
            }

            int nextSpace = remaining.IndexOf(' ', StringComparison.Ordinal);
            return nextSpace < 0 ? remaining : remaining[..nextSpace];
        }
    }

    private sealed class TempRuntimeWorkspace : IDisposable
    {
        private TempRuntimeWorkspace(string rootPath)
        {
            RootPath = rootPath;
            WorkingDirectoryPath = Path.Combine(rootPath, "work");
            MountedImagePath = Path.Combine(rootPath, "mount");
            UsbCacheRootPath = Path.Combine(rootPath, "usb-cache");
            Directory.CreateDirectory(WorkingDirectoryPath);
            Directory.CreateDirectory(MountedImagePath);
            Directory.CreateDirectory(UsbCacheRootPath);
        }

        public string RootPath { get; }
        public string WorkingDirectoryPath { get; }
        public string MountedImagePath { get; }
        public string UsbCacheRootPath { get; }

        public static TempRuntimeWorkspace Create()
        {
            return new TempRuntimeWorkspace(Path.Combine(Path.GetTempPath(), $"foundry-runtime-{Guid.NewGuid():N}"));
        }

        public string CreateArchive(string archiveName, string executableName)
        {
            string payloadPath = Path.Combine(RootPath, "payloads", Path.GetFileNameWithoutExtension(archiveName));
            Directory.CreateDirectory(payloadPath);
            File.WriteAllText(Path.Combine(payloadPath, executableName), executableName);

            string archivePath = Path.Combine(RootPath, archiveName);
            ZipFile.CreateFromDirectory(payloadPath, archivePath);
            return archivePath;
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

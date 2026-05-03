using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeIsoMediaServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenBootExIsEnabled_PassesBootExToMakeWinPeMedia()
    {
        using TempPreparedWorkspace temp = TempPreparedWorkspace.Create(useBootEx: true);
        string outputIsoPath = Path.Combine(temp.RootPath, "out", "foundry.iso");
        var runner = new FakeIsoRunner();
        var service = new WinPeIsoMediaService(runner);

        WinPeResult result = await service.CreateAsync(
            new WinPeIsoMediaOptions
            {
                PreparedWorkspace = temp.PreparedWorkspace,
                OutputIsoPath = outputIsoPath,
                IsoTempDirectoryPath = Path.Combine(temp.RootPath, "iso-temp")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeProcessExecution execution = Assert.Single(runner.Executions);
        Assert.Contains("/ISO", execution.Arguments);
        Assert.Contains("/F", execution.Arguments);
        Assert.Contains("/bootex", execution.Arguments);
        Assert.True(File.Exists(outputIsoPath));
    }

    [Fact]
    public async Task CreateAsync_WhenOutputPathContainsNonAscii_UsesAsciiSafeOutputAndCopiesBack()
    {
        using TempPreparedWorkspace temp = TempPreparedWorkspace.Create(useBootEx: false);
        string outputIsoPath = Path.Combine(temp.RootPath, "réseau", "foundry-é.iso");
        var runner = new FakeIsoRunner();
        var service = new WinPeIsoMediaService(runner);

        WinPeResult result = await service.CreateAsync(
            new WinPeIsoMediaOptions
            {
                PreparedWorkspace = temp.PreparedWorkspace,
                OutputIsoPath = outputIsoPath,
                IsoTempDirectoryPath = Path.Combine(temp.RootPath, "iso-temp")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(outputIsoPath));
        Assert.DoesNotContain("réseau", runner.Executions[0].Arguments);
        Assert.DoesNotContain("foundry-é.iso", runner.Executions[0].Arguments);
    }

    private sealed class TempPreparedWorkspace : IDisposable
    {
        private TempPreparedWorkspace(string rootPath, WinPeWorkspacePreparationResult preparedWorkspace)
        {
            RootPath = rootPath;
            PreparedWorkspace = preparedWorkspace;
        }

        public string RootPath { get; }
        public WinPeWorkspacePreparationResult PreparedWorkspace { get; }

        public static TempPreparedWorkspace Create(bool useBootEx)
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-iso-{Guid.NewGuid():N}");
            string media = Path.Combine(root, "work", "media");
            string bootWim = Path.Combine(media, "sources", "boot.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(bootWim)!);
            File.WriteAllText(bootWim, "wim");

            var artifact = new WinPeBuildArtifact
            {
                WorkingDirectoryPath = Path.Combine(root, "work"),
                MediaDirectoryPath = media,
                BootWimPath = bootWim,
                MountDirectoryPath = Path.Combine(root, "mount"),
                DriverWorkspacePath = Path.Combine(root, "drivers"),
                LogsDirectoryPath = Path.Combine(root, "logs"),
                MakeWinPeMediaPath = "MakeWinPEMedia.cmd",
                DismPath = "dism.exe",
                Architecture = WinPeArchitecture.X64
            };

            var tools = new WinPeToolPaths
            {
                MakeWinPeMediaPath = "MakeWinPEMedia.cmd",
                DismPath = "dism.exe"
            };

            return new TempPreparedWorkspace(
                root,
                new WinPeWorkspacePreparationResult
                {
                    Artifact = artifact,
                    Tools = tools,
                    UseBootEx = useBootEx
                });
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FakeIsoRunner : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            throw new NotSupportedException();
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            string outputIsoPath = ExtractLastIsoArgument(scriptArguments);
            Directory.CreateDirectory(Path.GetDirectoryName(outputIsoPath)!);
            File.WriteAllText(outputIsoPath, "iso");

            var execution = new WinPeProcessExecution
            {
                FileName = scriptPath,
                Arguments = scriptArguments,
                WorkingDirectory = workingDirectory
            };

            Executions.Add(execution);
            return Task.FromResult(execution);
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        private static string ExtractLastIsoArgument(string arguments)
        {
            int isoIndex = arguments.LastIndexOf(".iso", StringComparison.OrdinalIgnoreCase);
            Assert.True(isoIndex >= 0, $"No ISO argument found in: {arguments}");

            int end = isoIndex + ".iso".Length;
            int start = arguments.LastIndexOf(' ', isoIndex);
            start = start < 0 ? 0 : start + 1;
            return arguments[start..end].Trim('"');
        }
    }
}

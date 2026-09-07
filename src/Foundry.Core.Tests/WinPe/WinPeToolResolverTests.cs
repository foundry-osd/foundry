// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeToolResolverTests
{
    [Fact]
    public void ResolveTools_WhenNativeAdkDismIsMissing_DoesNotUseSystemDism()
    {
        using var directory = new TemporaryDirectory();
        string winPeRoot = Path.Combine(directory.Path, "Assessment and Deployment Kit", "Windows Preinstallation Environment");
        Directory.CreateDirectory(winPeRoot);
        File.WriteAllText(Path.Combine(winPeRoot, "copype.cmd"), string.Empty);
        File.WriteAllText(Path.Combine(winPeRoot, "MakeWinPEMedia.cmd"), string.Empty);

        WinPeResult<WinPeToolPaths> result = new WinPeToolResolver(() => null).ResolveTools(directory.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal("dism", result.Error?.ToolName);
    }

    [Fact]
    public void ResolveTools_WhenKitsRootCannotBeFound_ReturnsToolNotFound()
    {
        var resolver = new WinPeToolResolver(() => null);

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools();

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
        Assert.Equal(WinPeFailureKinds.Tooling, result.Error?.FailureKind);
        Assert.Equal(WinPeFailureReasons.ToolNotFound, result.Error?.FailureReason);
        Assert.Equal("Windows ADK", result.Error?.ToolName);
    }

    [Fact]
    public void ResolveTools_WhenWinPeToolsAreMissing_ReturnsToolNotFound()
    {
        using var tempDirectory = new TemporaryDirectory();
        var resolver = new WinPeToolResolver(() => null);

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools(tempDirectory.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
    }

    [Theory]
    [InlineData(Architecture.X64, WinPeArchitecture.Arm64, "amd64", Machine.Amd64)]
    [InlineData(Architecture.Arm64, WinPeArchitecture.X64, "arm64", Machine.Arm64)]
    public void ResolveTools_WhenToolsExist_ReturnsHostNativePaths(Architecture host, WinPeArchitecture target, string folder, Machine machine)
    {
        using var tempDirectory = new TemporaryDirectory();
        string winPeRoot = Path.Combine(tempDirectory.Path, "Assessment and Deployment Kit", "Windows Preinstallation Environment");
        Directory.CreateDirectory(winPeRoot);
        string copypePath = Path.Combine(winPeRoot, "copype.cmd");
        string makeWinPeMediaPath = Path.Combine(winPeRoot, "MakeWinPEMedia.cmd");
        File.WriteAllText(copypePath, string.Empty);
        File.WriteAllText(makeWinPeMediaPath, string.Empty);

        string dismPath = Path.Combine(tempDirectory.Path, "Assessment and Deployment Kit", "Deployment Tools", folder, "DISM", "dism.exe");
        PortableExecutableFixture.Write(dismPath, machine);
        var resolver = new WinPeToolResolver(() => null, () => host, _ => new Version(10, 0, 26100, 2454));

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools(tempDirectory.Path, target);

        Assert.True(result.IsSuccess);
        Assert.Equal(tempDirectory.Path, result.Value?.KitsRootPath);
        Assert.Equal(copypePath, result.Value?.CopypePath);
        Assert.Equal(makeWinPeMediaPath, result.Value?.MakeWinPeMediaPath);
        Assert.Equal(dismPath, result.Value?.DismPath);
        Assert.Equal(host, result.Value?.HostArchitecture);
        Assert.Equal(target, result.Value?.TargetArchitecture);
        Assert.EndsWith("cmd.exe", result.Value?.CmdPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("x64", "10.0.26100.9999", 26100, true)]
    [InlineData("arm64", "10.0.26100.1", 26100, false)]
    [InlineData("x64", "10.0.26200.1", 26100, true)]
    [InlineData("arm64", "10.0.26200.1", 26100, false)]
    [InlineData("x64", "10.0.22000.1", 26100, true)]
    [InlineData("x64", "10.0.22621.1", 26100, true)]
    [InlineData("x64", "10.0.22631.1", 26100, true)]
    [InlineData("x64", "10.0.25000.1", 26100, false)]
    [InlineData("x64", "10.0.26300.1", 30000, false)]
    [InlineData("x64", "10.0.28000.1", 30000, false)]
    [InlineData("x64", "10.0.19041.1", 26100, false)]
    [InlineData("x64", "invalid", 26100, false)]
    [InlineData("x64", "", 26100, false)]
    public void ValidateImageMetadata_RequiresSupportedArchitectureAndServicingBuild(string architecture, string version, int toolBuild, bool success)
    {
        var tools = new WinPeToolPaths { TargetArchitecture = WinPeArchitecture.X64, DismVersion = new(10, 0, toolBuild, 1) };
        WinPeResult result = WinPeToolResolver.ValidateImageMetadata(tools, WinPeArchitecture.X64,
            $"Deployment Image Servicing and Management tool\nVersion: 10.0.26100.1\nDetails for image : fixture.wim\nArchitecture : {architecture}\nVersion : {version}\n");
        Assert.Equal(success, result.IsSuccess);
    }

    [Theory]
    [InlineData(Machine.Arm64, 26100)]
    [InlineData(Machine.Amd64, 19041)]
    public void ResolveTools_RejectsIncompatibleNativeMachineOrServicingVersion(Machine machine, int build)
    {
        using var directory = new TemporaryDirectory();
        string adk = Path.Combine(directory.Path, "Assessment and Deployment Kit");
        string winPe = Path.Combine(adk, "Windows Preinstallation Environment");
        Directory.CreateDirectory(winPe);
        File.WriteAllText(Path.Combine(winPe, "copype.cmd"), string.Empty);
        File.WriteAllText(Path.Combine(winPe, "MakeWinPEMedia.cmd"), string.Empty);
        PortableExecutableFixture.Write(Path.Combine(adk, "Deployment Tools", "amd64", "DISM", "dism.exe"), machine);
        var resolver = new WinPeToolResolver(() => null, () => Architecture.X64, _ => new(10, 0, build));

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools(directory.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal("dism", result.Error?.ToolName);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateImageAsync_OptionalLoggingDoesNotCreatePreCopyWorkspace(bool writeDiagnosticLog)
    {
        using var directory = new TemporaryDirectory();
        string working = Path.Combine(directory.Path, "not-created-yet");
        var tools = new WinPeToolPaths
        {
            DismPath = "fixture-dism.exe",
            TargetArchitecture = WinPeArchitecture.X64,
            DismVersion = new(10, 0, 26100, 1)
        };
        WinPeResult result = await WinPeToolResolver.ValidateImageAsync(tools, "fixture.wim", 1,
            WinPeArchitecture.X64, new MetadataRunner(), working, TestContext.Current.CancellationToken, writeDiagnosticLog);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(writeDiagnosticLog, Directory.Exists(working));
    }

    private sealed class MetadataRunner : IWinPeProcessRunner
    {
        public Task<WinPeProcessExecution> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
            CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null, TimeSpan? executionTimeout = null) =>
            Task.FromResult(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = string.Join(' ', arguments),
                WorkingDirectory = workingDirectory,
                StandardOutput = "Architecture : x64\nVersion : 10.0.26100\nServicePack Build : 9999\n"
            });

        public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory,
            CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory,
            CancellationToken cancellationToken, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory,
            CancellationToken cancellationToken, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
    }
}

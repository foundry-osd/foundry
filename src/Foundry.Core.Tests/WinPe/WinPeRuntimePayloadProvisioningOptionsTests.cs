// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningOptionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DomainMediaRetainsExplicitPostInstallSourceWithoutWinPeDebug(bool archive)
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => key == (archive ? WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallArchive : WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallProject)
                ? archive ? "selected.zip" : "selected.csproj" : null).WithDomainJoinRuntime(true);
        Assert.True(options.PostInstall.IsEnabled);
        Assert.Equal(archive ? "selected.zip" : "", options.PostInstall.ArchivePath);
        Assert.Equal(archive ? "" : "selected.csproj", options.PostInstall.ProjectPath);
        Assert.Equal(WinPeProvisioningSource.Debug, options.PostInstall.ProvisioningSource);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerAttachedAndProjectsExist_EnablesDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        string winPeProjectPath = root.CreateProject("Foundry.WinPE");
        string postInstallProjectPath = root.CreateProject("Foundry.PostInstall");

        WinPeRuntimePayloadProvisioningOptions options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64,
            workingDirectoryPath: Path.Combine(root.RootPath, "work"),
            mountedImagePath: Path.Combine(root.RootPath, "mount"),
            usbCacheRootPath: Path.Combine(root.RootPath, "usb"),
            releaseVersion: "26.11.2.1",
            isDebuggerAttached: true,
            getEnvironmentVariable: _ => null,
            projectDiscoveryStartPath: root.RootPath);

        Assert.Equal("26.11.2.1", options.ReleaseVersion);
        Assert.True(options.WinPe.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.WinPe.ProvisioningSource);
        Assert.Equal(winPeProjectPath, options.WinPe.ProjectPath);
        Assert.True(options.PostInstall.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.PostInstall.ProvisioningSource);
        Assert.Equal(postInstallProjectPath, options.PostInstall.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerIsNotAttached_DoesNotAutoEnableDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        root.CreateProject("Foundry.WinPE");
        root.CreateProject("Foundry.PostInstall");
        root.CreateProject("Foundry.AutopilotInteractive");

        WinPeRuntimePayloadProvisioningOptions options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64,
            workingDirectoryPath: Path.Combine(root.RootPath, "work"),
            mountedImagePath: Path.Combine(root.RootPath, "mount"),
            usbCacheRootPath: Path.Combine(root.RootPath, "usb"),
            releaseVersion: "26.11.2.1",
            isDebuggerAttached: false,
            getEnvironmentVariable: _ => null,
            projectDiscoveryStartPath: root.RootPath);

        Assert.False(options.WinPe.IsEnabled);
        Assert.False(options.PostInstall.IsEnabled);
        Assert.False(options.AutopilotInteractive.IsEnabled);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    public void CreateDeveloperOptions_WhenWinPeIsExplicitlyEnabled_PreservesDebugSourceWithoutProject(string flag)
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => key == WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable ? flag : null);
        Assert.True(options.WinPe.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.WinPe.ProvisioningSource);
        Assert.Empty(options.WinPe.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenWinPeArchiveIsSet_PrefersArchiveOverProject()
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.Arm64, "work", "mount", "usb", "26.11.2.1", false,
            key => key switch
            {
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeArchive => "winpe.zip",
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeProject => "winpe.csproj",
                _ => null
            });
        Assert.True(options.WinPe.IsEnabled);
        Assert.Equal("winpe.zip", options.WinPe.ArchivePath);
        Assert.Empty(options.WinPe.ProjectPath);
        Assert.Equal(WinPeProvisioningSource.Debug, options.WinPe.ProvisioningSource);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenWinPeProjectIsSet_DiscoversDebugApplicationsBesideIt()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        string winPeProjectPath = root.CreateProject("Foundry.WinPE");
        string postInstallProjectPath = root.CreateProject("Foundry.PostInstall");
        string autopilotProjectPath = root.CreateProject("Foundry.AutopilotInteractive");

        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => key switch
            {
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable => "1",
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeProject => winPeProjectPath,
                _ => null
            });

        Assert.Equal(winPeProjectPath, options.WinPe.ProjectPath);
        Assert.Equal(postInstallProjectPath, options.PostInstall.ProjectPath);
        Assert.Equal(autopilotProjectPath, options.AutopilotInteractive.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerHasNoWinPeProject_KeepsWinPeDebugEnabled()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", true,
            _ => null, root.RootPath);
        Assert.True(options.WinPe.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.WinPe.ProvisioningSource);
        Assert.Empty(options.WinPe.ProjectPath);
        Assert.False(options.AutopilotInteractive.IsEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateDeveloperOptions_PostInstallAndAutopilotInteractiveFollowTheWinPeDebugFlag(bool winPeDebug)
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        string postInstallProjectPath = root.CreateProject("Foundry.PostInstall");
        string autopilotProjectPath = root.CreateProject("Foundry.AutopilotInteractive");

        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => winPeDebug && key == WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable ? "1" : null,
            root.RootPath);

        Assert.Equal(winPeDebug, options.WinPe.IsEnabled);
        Assert.Equal(winPeDebug, options.PostInstall.IsEnabled);
        Assert.Equal(winPeDebug, options.AutopilotInteractive.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.PostInstall.ProvisioningSource);
        Assert.Equal(winPeDebug ? postInstallProjectPath : string.Empty, options.PostInstall.ProjectPath);
        Assert.Equal(winPeDebug ? autopilotProjectPath : string.Empty, options.AutopilotInteractive.ProjectPath);
        Assert.Equal(winPeDebug, options.WithDomainJoinRuntime(true).PostInstall.ProvisioningSource == WinPeProvisioningSource.Debug);
    }

    [Fact]
    public void CreateDeveloperOptions_PrefersAutopilotInteractiveArchiveOverProject()
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => key switch
            {
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable => "1",
                WinPeRuntimePayloadEnvironmentVariables.DebugAutopilotInteractiveArchive => "autopilot.zip",
                WinPeRuntimePayloadEnvironmentVariables.DebugAutopilotInteractiveProject => "autopilot.csproj",
                _ => null
            });

        Assert.True(options.AutopilotInteractive.IsEnabled);
        Assert.Equal("autopilot.zip", options.AutopilotInteractive.ArchivePath);
        Assert.Empty(options.AutopilotInteractive.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_KeepsAutopilotInteractiveDisabled_WhenWinPeIsDebugButNoSourceExists()
    {
        using TempProjectRoot root = TempProjectRoot.Create();

        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", "26.11.2.1", false,
            key => key == WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable ? "1" : null,
            root.RootPath);

        Assert.True(options.WinPe.IsEnabled);
        Assert.False(options.AutopilotInteractive.IsEnabled);
    }

    private sealed class TempProjectRoot : IDisposable
    {
        private TempProjectRoot(string rootPath)
        {
            RootPath = rootPath;
            Directory.CreateDirectory(rootPath);
        }

        public string RootPath { get; }

        public static TempProjectRoot Create()
        {
            return new TempProjectRoot(Path.Combine(Path.GetTempPath(), $"foundry-runtime-options-{Guid.NewGuid():N}"));
        }

        public string CreateProject(string projectName)
        {
            string projectPath = Path.Combine(RootPath, "src", projectName, $"{projectName}.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, "<Project />");
            return projectPath;
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

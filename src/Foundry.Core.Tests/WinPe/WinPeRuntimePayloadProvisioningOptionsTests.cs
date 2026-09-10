// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningOptionsTests
{
    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerAttachedAndProjectsExist_EnablesDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        string bootstrapProjectPath = root.CreateProject("Foundry.Bootstrap");
        string connectProjectPath = root.CreateProject("Foundry.Connect");
        string deployProjectPath = root.CreateProject("Foundry.Deploy");

        WinPeRuntimePayloadProvisioningOptions options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64,
            workingDirectoryPath: Path.Combine(root.RootPath, "work"),
            mountedImagePath: Path.Combine(root.RootPath, "mount"),
            usbCacheRootPath: Path.Combine(root.RootPath, "usb"),
            isDebuggerAttached: true,
            getEnvironmentVariable: _ => null,
            projectDiscoveryStartPath: root.RootPath);

        Assert.True(options.Connect.IsEnabled);
        Assert.True(options.Deploy.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Connect.ProvisioningSource);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Deploy.ProvisioningSource);
        Assert.True(options.Bootstrap.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Bootstrap.ProvisioningSource);
        Assert.Equal(bootstrapProjectPath, options.Bootstrap.ProjectPath);
        Assert.Equal(connectProjectPath, options.Connect.ProjectPath);
        Assert.Equal(deployProjectPath, options.Deploy.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerIsNotAttached_DoesNotAutoEnableDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        root.CreateProject("Foundry.Bootstrap");
        root.CreateProject("Foundry.Connect");
        root.CreateProject("Foundry.Deploy");

        WinPeRuntimePayloadProvisioningOptions options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64,
            workingDirectoryPath: Path.Combine(root.RootPath, "work"),
            mountedImagePath: Path.Combine(root.RootPath, "mount"),
            usbCacheRootPath: Path.Combine(root.RootPath, "usb"),
            isDebuggerAttached: false,
            getEnvironmentVariable: _ => null,
            projectDiscoveryStartPath: root.RootPath);

        Assert.False(options.Bootstrap.IsEnabled);
        Assert.False(options.Connect.IsEnabled);
        Assert.False(options.Deploy.IsEnabled);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenArchiveOverrideIsSet_PrefersArchiveOverProject()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        root.CreateProject("Foundry.Connect");
        string archivePath = Path.Combine(root.RootPath, "connect.zip");

        WinPeRuntimePayloadProvisioningOptions options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64,
            workingDirectoryPath: Path.Combine(root.RootPath, "work"),
            mountedImagePath: Path.Combine(root.RootPath, "mount"),
            usbCacheRootPath: Path.Combine(root.RootPath, "usb"),
            isDebuggerAttached: true,
            getEnvironmentVariable: key => key switch
            {
                WinPeRuntimePayloadEnvironmentVariables.DebugConnectArchive => archivePath,
                _ => null
            },
            projectDiscoveryStartPath: root.RootPath);

        Assert.True(options.Connect.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Connect.ProvisioningSource);
        Assert.Equal(archivePath, options.Connect.ArchivePath);
        Assert.Empty(options.Connect.ProjectPath);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    public void CreateDeveloperOptions_WhenBootstrapIsExplicitlyEnabled_PreservesDebugSourceWithoutProject(string flag)
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", false,
            key => key == WinPeRuntimePayloadEnvironmentVariables.DebugBootstrapEnable ? flag : null);
        Assert.True(options.Bootstrap.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Bootstrap.ProvisioningSource);
        Assert.Empty(options.Bootstrap.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenBootstrapArchiveIsSet_PrefersArchiveOverProject()
    {
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.Arm64, "work", "mount", "usb", false,
            key => key switch
            {
                WinPeRuntimePayloadEnvironmentVariables.DebugBootstrapArchive => "bootstrap.zip",
                WinPeRuntimePayloadEnvironmentVariables.DebugBootstrapProject => "bootstrap.csproj",
                _ => null
            });
        Assert.True(options.Bootstrap.IsEnabled);
        Assert.Equal("bootstrap.zip", options.Bootstrap.ArchivePath);
        Assert.Empty(options.Bootstrap.ProjectPath);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Bootstrap.ProvisioningSource);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerHasNoBootstrapProject_KeepsBootstrapDebugEnabled()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
        var options = WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            WinPeArchitecture.X64, "work", "mount", "usb", true,
            _ => null, root.RootPath);
        Assert.True(options.Bootstrap.IsEnabled);
        Assert.Equal(WinPeProvisioningSource.Debug, options.Bootstrap.ProvisioningSource);
        Assert.Empty(options.Bootstrap.ProjectPath);
        Assert.False(options.Connect.IsEnabled);
        Assert.False(options.Deploy.IsEnabled);
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

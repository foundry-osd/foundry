using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningOptionsTests
{
    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerAttachedAndProjectsExist_EnablesDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
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
        Assert.Equal(connectProjectPath, options.Connect.ProjectPath);
        Assert.Equal(deployProjectPath, options.Deploy.ProjectPath);
    }

    [Fact]
    public void CreateDeveloperOptions_WhenDebuggerIsNotAttached_DoesNotAutoEnableDebugRuntimes()
    {
        using TempProjectRoot root = TempProjectRoot.Create();
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

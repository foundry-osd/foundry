// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveProvisioningServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));

    public AutopilotInteractiveProvisioningServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Provision_InstallsRuntimeAndWritesConfigInTheRetainedLayout()
    {
        string windowsRoot = Path.Combine(_root, "Target");
        var configuration = new AutopilotInteractiveRuntimeConfiguration
        {
            AssignedComputerName = "FINAL-PC",
            Theme = RuntimeThemePreference.Dark,
            CultureName = "fr-FR",
            DiagnosticSessionId = "SESSION1"
        };

        AutopilotInteractiveProvisioningResult result = await CreateService().ProvisionAsync(windowsRoot,
            AutopilotInteractiveTestRuntime.CreateLocalOrigin(_root), configuration, null, TestContext.Current.CancellationToken);

        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(windowsRoot);
        Assert.Equal(layout.RuntimeAutopilotInteractive, result.RuntimeDirectory);
        Assert.Equal(Path.Combine(layout.RuntimeAutopilotInteractive, "Foundry.AutopilotInteractive.exe"), result.ExecutablePath);
        Assert.Equal(Path.Combine(layout.RuntimeAutopilotInteractive, "autopilot-interactive.json"), result.ConfigPath);
        Assert.True(File.Exists(result.ExecutablePath));
        Assert.True(Directory.Exists(layout.StateAutopilotInteractive));
        Assert.True(Directory.Exists(layout.LogsAutopilotInteractive));
        Assert.Empty(Directory.GetFiles(result.RuntimeDirectory, "*.zip"));
        byte[] bytes = File.ReadAllBytes(result.ConfigPath);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal(configuration, AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(Encoding.UTF8.GetString(bytes)));
    }

    [Fact]
    public void RelativePaths_MatchTheStagedLayout()
    {
        string windowsRoot = Path.Combine(_root, "Target");
        string retained = DeploymentStorageLayout.FromPartitionRoot(windowsRoot).Root;
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(windowsRoot);

        Assert.Equal("Runtime/AutopilotInteractive/Foundry.AutopilotInteractive.exe", AutopilotInteractiveProvisioningService.RuntimeExecutableRelativePath);
        Assert.Equal("Runtime/AutopilotInteractive/autopilot-interactive.json", AutopilotInteractiveProvisioningService.RuntimeConfigRelativePath);
        Assert.Equal(Path.Combine(retained, "Runtime", "AutopilotInteractive"), layout.RuntimeAutopilotInteractive);
        Assert.Equal(Path.Combine(retained, "State", "AutopilotInteractive"), layout.StateAutopilotInteractive);
        Assert.Equal(Path.Combine(retained, "Logs", "AutopilotInteractive"), layout.LogsAutopilotInteractive);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AutopilotInteractiveProvisioningService CreateService() => new(new AutopilotInteractiveRuntimeSource(
        Path.Combine(_root, "WinPE"), "26.11.3.1", new HttpClient(),
        new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance), _ => null));
}

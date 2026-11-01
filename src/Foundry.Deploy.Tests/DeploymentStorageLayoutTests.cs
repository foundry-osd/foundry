// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStorageLayoutTests
{
    [Fact]
    public void Layout_UsesSelectedOfflineWindowsAndDoesNotCreateUnusedDirectories()
    {
        var layout = DeploymentStorageLayout.FromPartitionRoot(@"R:\");
        string[] paths = [layout.RuntimePreOobe, layout.RuntimeAutopilotInteractive,
            layout.PayloadsDrivers, layout.PayloadsNetworkProfiles,
            layout.StateDeployment, layout.StatePreOobe, layout.StateAutopilotInteractive,
            layout.LogsDeployment, layout.LogsPreOobe, layout.LogsAutopilotInteractive, layout.LogsAutopilotHash];
        Assert.All(paths, path => Assert.StartsWith(@"R:\Windows\Temp\Foundry\", path));
        Assert.All(paths, path => Assert.DoesNotContain("ProgramData", path));
        string temporary = Path.Combine(Path.GetTempPath(), "FoundryDeployTests", Guid.NewGuid().ToString("N"));
        _ = DeploymentStorageLayout.FromPartitionRoot(temporary);
        Assert.False(Directory.Exists(temporary));
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Autopilot;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Owns post-deployment paths in the selected Windows installation; constructing a layout creates no directories.</summary>
public sealed class DeploymentStorageLayout
{
    /// <summary>Creates a layout rooted at an offline Windows directory, never the currently booted WinPE Windows directory.</summary>
    public DeploymentStorageLayout(string offlineWindowsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offlineWindowsRoot);
        Root = Path.Combine(offlineWindowsRoot, "Temp", "Foundry");
    }

    /// <summary>Creates a layout for the selected target partition.</summary>
    public static DeploymentStorageLayout FromPartitionRoot(string partitionRoot) => new(Path.Combine(partitionRoot, "Windows"));

    /// <summary>Gets the common post-deployment ownership root.</summary>
    public string Root { get; }

    /// <summary>Gets the PreOobe runtime directory.</summary>
    public string RuntimePreOobe => Path.Combine(Root, "Runtime", "PreOobe");

    /// <summary>Gets the Foundry.AutopilotInteractive runtime directory, holding its executable and configuration.</summary>
    public string RuntimeAutopilotInteractive => Combine(AutopilotInteractiveLayout.RuntimeRelativePath);

    /// <summary>Gets the Drivers payloads directory.</summary>
    public string PayloadsDrivers => Path.Combine(Root, "Payloads", "Drivers");

    /// <summary>Gets the NetworkProfiles payloads directory.</summary>
    public string PayloadsNetworkProfiles => Path.Combine(Root, "Payloads", "NetworkProfiles");

    /// <summary>Gets the Deployment state directory.</summary>
    public string StateDeployment => Path.Combine(Root, "State", "Deployment");

    /// <summary>Gets the PreOobe state directory.</summary>
    public string StatePreOobe => Path.Combine(Root, "State", "PreOobe");

    /// <summary>Gets the Foundry.AutopilotInteractive state directory, holding its registration result.</summary>
    public string StateAutopilotInteractive => Combine(AutopilotInteractiveLayout.StateRelativePath);

    /// <summary>Gets the Deployment logs directory.</summary>
    public string LogsDeployment => Path.Combine(Root, "Logs", "Deployment");

    /// <summary>Gets the PreOobe logs directory.</summary>
    public string LogsPreOobe => Path.Combine(Root, "Logs", "PreOobe");

    /// <summary>Gets the Foundry.AutopilotInteractive logs directory.</summary>
    public string LogsAutopilotInteractive => Combine(AutopilotInteractiveLayout.LogsRelativePath);

    /// <summary>Gets the AutopilotHash logs directory.</summary>
    public string LogsAutopilotHash => Path.Combine(Root, "Logs", "AutopilotHash");

    private string Combine(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}

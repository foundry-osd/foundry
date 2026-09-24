// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

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

    /// <summary>Gets the AutopilotRegistration runtime directory.</summary>
    public string RuntimeAutopilotRegistration => Path.Combine(Root, "Runtime", "AutopilotRegistration");

    /// <summary>Gets the Drivers payloads directory.</summary>
    public string PayloadsDrivers => Path.Combine(Root, "Payloads", "Drivers");

    /// <summary>Gets the NetworkProfiles payloads directory.</summary>
    public string PayloadsNetworkProfiles => Path.Combine(Root, "Payloads", "NetworkProfiles");

    /// <summary>Gets the Customization payloads directory.</summary>
    public string PayloadsCustomization => Path.Combine(Root, "Payloads", "Customization");

    /// <summary>Gets the Deployment state directory.</summary>
    public string StateDeployment => Path.Combine(Root, "State", "Deployment");

    /// <summary>Gets the PreOobe state directory.</summary>
    public string StatePreOobe => Path.Combine(Root, "State", "PreOobe");

    /// <summary>Gets the AutopilotRegistration state directory.</summary>
    public string StateAutopilotRegistration => Path.Combine(Root, "State", "AutopilotRegistration");

    /// <summary>Gets the Deployment logs directory.</summary>
    public string LogsDeployment => Path.Combine(Root, "Logs", "Deployment");

    /// <summary>Gets the PreOobe logs directory.</summary>
    public string LogsPreOobe => Path.Combine(Root, "Logs", "PreOobe");

    /// <summary>Gets the AutopilotRegistration logs directory.</summary>
    public string LogsAutopilotRegistration => Path.Combine(Root, "Logs", "AutopilotRegistration");

    /// <summary>Gets the AutopilotHash logs directory.</summary>
    public string LogsAutopilotHash => Path.Combine(Root, "Logs", "AutopilotHash");

    /// <summary>Builds a CMD environment-variable path resolved only in the installed OS.</summary>
    public static string RuntimePath(string relativePath) => @"%SystemRoot%\Temp\Foundry\" + relativePath;
}

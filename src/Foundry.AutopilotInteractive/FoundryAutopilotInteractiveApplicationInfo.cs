// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.AutopilotInteractive;

/// <summary>
/// Build-time identity of Foundry.AutopilotInteractive.
/// </summary>
public static class FoundryAutopilotInteractiveApplicationInfo
{
    /// <summary>Display version: the informational version of the assembly, as the other WPF runtimes show it.</summary>
    public static string Version { get; } = WpfApplicationIdentity.FromAssembly(typeof(App).Assembly).Version;
}

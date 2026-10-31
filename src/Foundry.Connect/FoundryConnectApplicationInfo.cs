// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.Connect;

public static class FoundryConnectApplicationInfo
{
    public const string AppName = "Foundry Connect";
    public const string AboutTitle = "About Foundry Connect";
    public const string WindowTitle = "Foundry Connect";
    public const string DescriptionLine1 = "Foundry Connect validates network readiness in WinPE before the Foundry bootstrap continues.";
    public const string DescriptionLine2 = "This software is provided as-is. Confirm network access before continuing deployment.";
    public const int DefaultAutoContinueDelaySeconds = 10;
    public const int DefaultRefreshIntervalSeconds = 10;

    public static string Version { get; } = WpfApplicationIdentity.FromAssembly(typeof(App).Assembly).Version;
}

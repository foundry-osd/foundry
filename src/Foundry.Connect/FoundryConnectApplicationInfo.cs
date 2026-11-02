// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.Connect;

public static class FoundryConnectApplicationInfo
{
    public const int DefaultAutoContinueDelaySeconds = 10;
    public const int DefaultRefreshIntervalSeconds = 10;

    public static string Version { get; } = WpfApplicationIdentity.FromAssembly(typeof(App).Assembly).Version;
}

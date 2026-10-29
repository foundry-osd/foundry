// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.Deploy;

public static class FoundryDeployApplicationInfo
{
    public static string Version { get; } = WpfApplicationIdentity.FromAssembly(typeof(App).Assembly).Version;
}

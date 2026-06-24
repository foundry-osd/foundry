// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace Foundry.Deploy;

public static class FoundryDeployApplicationInfo
{
    private static readonly string AppVersion = ResolveVersion();

    public static string Version => AppVersion;

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(FoundryDeployApplicationInfo).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}

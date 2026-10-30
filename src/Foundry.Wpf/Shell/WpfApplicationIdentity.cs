// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace Foundry.Wpf.Shell;

/// <summary>Identifies the hosting WPF application to shared building blocks such as the About dialog.</summary>
/// <param name="Version">Display version of the hosting application.</param>
public sealed record WpfApplicationIdentity(string Version)
{
    private const string FallbackVersion = "0.0.0.0";

    /// <summary>
    /// Resolves the identity from an assembly: the trimmed informational version when present,
    /// otherwise the assembly version, otherwise <c>0.0.0.0</c>.
    /// </summary>
    public static WpfApplicationIdentity FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new WpfApplicationIdentity(informationalVersion.Trim());
        }

        return new WpfApplicationIdentity(assembly.GetName().Version?.ToString() ?? FallbackVersion);
    }
}

using System.Reflection;

namespace Foundry.Connect;

public static class FoundryConnectApplicationInfo
{
    private static readonly string AppVersion = ResolveVersion();

    public const string AppName = "Foundry Connect";
    public const string AboutTitle = "About Foundry Connect";
    public const string WindowTitle = "Foundry Connect";
    public const string DescriptionLine1 = "Foundry Connect validates network readiness in WinPE before the Foundry bootstrap continues.";
    public const string DescriptionLine2 = "This software is provided as-is. Confirm network access before continuing deployment.";
    public const string Footer = "Copyright (c) 2026 Foundry Contributors. Licensed under the MIT License.";
    public const int DefaultAutoContinueDelaySeconds = 10;
    public const int DefaultRefreshIntervalSeconds = 10;

    public static string Version => AppVersion;

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(FoundryConnectApplicationInfo).Assembly;
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

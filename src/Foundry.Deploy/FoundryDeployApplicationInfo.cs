using System.Reflection;

namespace Foundry.Deploy;

public static class FoundryDeployApplicationInfo
{
    private static readonly string AppVersion = ResolveVersion();

    public const string AppName = "Foundry Deploy";
    public const string DescriptionLine1 = "Foundry Deploy is the WinPE deployment experience for Foundry.";
    public const string DescriptionLine2 = "This software is provided as-is. Review your deployment settings before use.";
    public const string Footer = "Copyright (c) Project Foundry. All rights reserved.";

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

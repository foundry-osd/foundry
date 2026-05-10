namespace Foundry.Core.Services.WinPe;

public sealed record WinPeRuntimePayloadProvisioningOptions
{
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public string WorkingDirectoryPath { get; init; } = string.Empty;
    public string MountedImagePath { get; init; } = string.Empty;
    public string UsbCacheRootPath { get; init; } = string.Empty;
    public WinPeRuntimePayloadApplicationOptions Connect { get; init; } = new();
    public WinPeRuntimePayloadApplicationOptions Deploy { get; init; } = new();

    public static WinPeRuntimePayloadProvisioningOptions CreateDeveloperOptions(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        string mountedImagePath,
        string usbCacheRootPath,
        bool isDebuggerAttached,
        Func<string, string?>? getEnvironmentVariable = null,
        string? projectDiscoveryStartPath = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        return new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workingDirectoryPath,
            MountedImagePath = mountedImagePath,
            UsbCacheRootPath = usbCacheRootPath,
            Connect = CreateApplicationOptions(
                "Foundry.Connect",
                WinPeRuntimePayloadEnvironmentVariables.LocalConnectEnable,
                WinPeRuntimePayloadEnvironmentVariables.LocalConnectArchive,
                WinPeRuntimePayloadEnvironmentVariables.LocalConnectProject,
                isDebuggerAttached,
                getEnvironmentVariable,
                projectDiscoveryStartPath),
            Deploy = CreateApplicationOptions(
                "Foundry.Deploy",
                WinPeRuntimePayloadEnvironmentVariables.LocalDeployEnable,
                WinPeRuntimePayloadEnvironmentVariables.LocalDeployArchive,
                WinPeRuntimePayloadEnvironmentVariables.LocalDeployProject,
                isDebuggerAttached,
                getEnvironmentVariable,
                projectDiscoveryStartPath)
        };
    }

    private static WinPeRuntimePayloadApplicationOptions CreateApplicationOptions(
        string applicationName,
        string enableVariableName,
        string archiveVariableName,
        string projectVariableName,
        bool isDebuggerAttached,
        Func<string, string?> getEnvironmentVariable,
        string? projectDiscoveryStartPath)
    {
        string archivePath = (getEnvironmentVariable(archiveVariableName) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            return new WinPeRuntimePayloadApplicationOptions
            {
                IsEnabled = true,
                ArchivePath = archivePath
            };
        }

        string projectPath = (getEnvironmentVariable(projectVariableName) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            projectPath = TryFindProjectPath(applicationName, projectDiscoveryStartPath, out string discoveredProjectPath)
                ? discoveredProjectPath
                : string.Empty;
        }

        bool isExplicitlyEnabled = IsEnabledEnvironmentFlag(getEnvironmentVariable(enableVariableName));
        bool shouldAutoEnable = isDebuggerAttached && !string.IsNullOrWhiteSpace(projectPath);

        return new WinPeRuntimePayloadApplicationOptions
        {
            IsEnabled = isExplicitlyEnabled || shouldAutoEnable,
            ProjectPath = projectPath
        };
    }

    private static bool TryFindProjectPath(
        string applicationName,
        string? projectDiscoveryStartPath,
        out string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectDiscoveryStartPath))
        {
            projectPath = string.Empty;
            return false;
        }

        var current = new DirectoryInfo(projectDiscoveryStartPath);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", applicationName, $"{applicationName}.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            current = current.Parent;
        }

        projectPath = string.Empty;
        return false;
    }

    private static bool IsEnabledEnvironmentFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false
        };
    }
}

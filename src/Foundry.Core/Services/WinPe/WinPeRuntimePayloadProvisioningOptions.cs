namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes how Foundry.Connect and Foundry.Deploy runtime payloads are staged into a mounted boot image.
/// </summary>
public sealed record WinPeRuntimePayloadProvisioningOptions
{
    /// <summary>
    /// Gets the target runtime architecture.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    /// <summary>
    /// Gets the working directory used for publish/archive extraction.
    /// </summary>
    public string WorkingDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the mounted image root path.
    /// </summary>
    public string MountedImagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the USB cache root path staged into the boot image.
    /// </summary>
    public string UsbCacheRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets Foundry.Connect payload options.
    /// </summary>
    public WinPeRuntimePayloadApplicationOptions Connect { get; init; } = new();

    /// <summary>
    /// Gets Foundry.Deploy payload options.
    /// </summary>
    public WinPeRuntimePayloadApplicationOptions Deploy { get; init; } = new();

    /// <summary>
    /// Creates development-time payload options from debugger state and environment variables.
    /// </summary>
    /// <param name="architecture">The target runtime architecture.</param>
    /// <param name="workingDirectoryPath">The working directory used for payload preparation.</param>
    /// <param name="mountedImagePath">The mounted image root path.</param>
    /// <param name="usbCacheRootPath">The USB cache root path.</param>
    /// <param name="isDebuggerAttached">Whether a debugger is attached to the app process.</param>
    /// <param name="getEnvironmentVariable">An optional environment variable accessor for tests.</param>
    /// <param name="projectDiscoveryStartPath">An optional path used to discover sibling project files.</param>
    /// <returns>Runtime payload provisioning options.</returns>
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
                WinPeRuntimePayloadEnvironmentVariables.DebugConnectEnable,
                WinPeRuntimePayloadEnvironmentVariables.DebugConnectArchive,
                WinPeRuntimePayloadEnvironmentVariables.DebugConnectProject,
                isDebuggerAttached,
                getEnvironmentVariable,
                projectDiscoveryStartPath),
            Deploy = CreateApplicationOptions(
                "Foundry.Deploy",
                WinPeRuntimePayloadEnvironmentVariables.DebugDeployEnable,
                WinPeRuntimePayloadEnvironmentVariables.DebugDeployArchive,
                WinPeRuntimePayloadEnvironmentVariables.DebugDeployProject,
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

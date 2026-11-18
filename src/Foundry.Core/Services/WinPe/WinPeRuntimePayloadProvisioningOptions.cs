// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes how Foundry runtime payloads are staged into a mounted boot image. Every runtime ships in the boot image;
/// nothing is staged on the USB data volume and nothing is downloaded at boot.
/// </summary>
public sealed record WinPeRuntimePayloadProvisioningOptions
{
    /// <summary>Names the Foundry.WinPE application, its executable and its release asset prefix.</summary>
    public const string WinPeApplicationName = "Foundry.WinPE";

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
    /// Gets the informational version of the Foundry OSD build creating the media. It selects the GitHub release that
    /// release runtimes come from, so the media run the runtimes of that Foundry OSD version; it is required whenever a
    /// runtime is provisioned from a release.
    /// </summary>
    public string ReleaseVersion { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Foundry.WinPE payload. Its archive (the single-file executable, WPF's native libraries and the startup
    /// manifest) is extracted into <c>Foundry\WinPE</c> of the mounted image, so the image holds no archive; the executable
    /// runs the guardian, Connect and Deploy from the boot image.
    /// </summary>
    public WinPeRuntimePayloadApplicationOptions WinPe { get; init; } = new();

    /// <summary>
    /// Gets the release metadata shared by all provisioning stages of this media build.
    /// </summary>
    public WinPeRuntimeReleaseSnapshot? ReleaseSnapshot { get; init; }

    /// <summary>
    /// Gets Foundry.PostInstall payload options, following the WinPE debug selection. The executable, <c>Launch.cmd</c> and
    /// the runtime manifest of its archive are extracted into <c>Foundry\PostInstall</c> of the mounted image, so Foundry
    /// Deploy stages it into the target OS without any download.
    /// </summary>
    public WinPeRuntimePayloadApplicationOptions PostInstall { get; init; } = new();

    /// <summary>
    /// Gets Foundry.AutopilotInteractive payload options. The application is provisioned only on debug media, from a local
    /// archive or project, and never from a release: release media do not carry it and Deploy downloads it into the target OS.
    /// Its archive is written to <c>Foundry\AutopilotInteractive\Foundry.AutopilotInteractive.zip</c> of the mounted image.
    /// </summary>
    public WinPeRuntimePayloadApplicationOptions AutopilotInteractive { get; init; } = new();

    /// <summary>
    /// Creates development-time payload options from debugger state and environment variables. Foundry.PostInstall is always
    /// enabled: from its local source on debug media or when one is set, else from the release.
    /// </summary>
    /// <param name="architecture">The target runtime architecture.</param>
    /// <param name="workingDirectoryPath">The working directory used for payload preparation.</param>
    /// <param name="mountedImagePath">The mounted image root path.</param>
    /// <param name="releaseVersion">The informational version of the Foundry OSD build creating the media.</param>
    /// <param name="isDebuggerAttached">Whether a debugger is attached to the app process.</param>
    /// <param name="getEnvironmentVariable">An optional environment variable accessor for tests.</param>
    /// <param name="projectDiscoveryStartPath">An optional path used to discover sibling project files.</param>
    /// <returns>Runtime payload provisioning options.</returns>
    public static WinPeRuntimePayloadProvisioningOptions CreateDeveloperOptions(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        string mountedImagePath,
        string releaseVersion,
        bool isDebuggerAttached,
        Func<string, string?>? getEnvironmentVariable = null,
        string? projectDiscoveryStartPath = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var options = new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workingDirectoryPath,
            MountedImagePath = mountedImagePath,
            ReleaseVersion = releaseVersion,
            WinPe = CreateApplicationOptions(
                WinPeApplicationName,
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable,
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeArchive,
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeProject,
                isDebuggerAttached,
                getEnvironmentVariable,
                projectDiscoveryStartPath,
                requireLocalSourceWhenDebugging: true)
        };
        // PostInstall and AutopilotInteractive follow the WinPE debug selection: debug boot media carry local builds of both.
        if (!options.WinPe.IsEnabled)
        {
            string archive = (getEnvironmentVariable(WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallArchive) ?? string.Empty).Trim();
            string project = (getEnvironmentVariable(WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallProject) ?? string.Empty).Trim();
            return options with
            {
                PostInstall = string.IsNullOrWhiteSpace(archive) && string.IsNullOrWhiteSpace(project)
                    ? new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
                    : new()
                    {
                        IsEnabled = true,
                        ArchivePath = archive,
                        ProjectPath = string.IsNullOrWhiteSpace(archive) ? project : string.Empty
                    }
            };
        }
        string? discoveryPath = string.IsNullOrWhiteSpace(options.WinPe.ProjectPath)
            ? projectDiscoveryStartPath : Path.GetDirectoryName(Path.GetFullPath(options.WinPe.ProjectPath));
        WinPeRuntimePayloadApplicationOptions autopilotInteractive = CreateApplicationOptions(
            "Foundry.AutopilotInteractive",
            WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable,
            WinPeRuntimePayloadEnvironmentVariables.DebugAutopilotInteractiveArchive,
            WinPeRuntimePayloadEnvironmentVariables.DebugAutopilotInteractiveProject,
            true, getEnvironmentVariable, discoveryPath);
        return options with
        {
            PostInstall = CreateApplicationOptions(
                "Foundry.PostInstall",
                WinPeRuntimePayloadEnvironmentVariables.DebugWinPeEnable,
                WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallArchive,
                WinPeRuntimePayloadEnvironmentVariables.DebugPostInstallProject,
                true, getEnvironmentVariable, discoveryPath, requireLocalSourceWhenDebugging: true),
            // Without a local source there is nothing to provision; the app is optional on debug media.
            AutopilotInteractive = autopilotInteractive with
            {
                IsEnabled = !string.IsNullOrWhiteSpace(autopilotInteractive.ArchivePath) ||
                    !string.IsNullOrWhiteSpace(autopilotInteractive.ProjectPath)
            }
        };
    }

    private static WinPeRuntimePayloadApplicationOptions CreateApplicationOptions(
        string applicationName,
        string enableVariableName,
        string archiveVariableName,
        string projectVariableName,
        bool isDebuggerAttached,
        Func<string, string?> getEnvironmentVariable,
        string? projectDiscoveryStartPath,
        bool requireLocalSourceWhenDebugging = false)
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
        bool shouldAutoEnable = isDebuggerAttached &&
            (requireLocalSourceWhenDebugging || !string.IsNullOrWhiteSpace(projectPath));

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

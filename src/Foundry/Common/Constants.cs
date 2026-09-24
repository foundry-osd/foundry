// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Common
{
    /// <summary>
    /// Defines application-wide constants and well-known local storage paths.
    /// </summary>
    public static partial class Constants
    {
        /// <summary>
        /// Gets the application folder and process identity name.
        /// </summary>
        public const string ApplicationName = "Foundry";

        /// <summary>
        /// Gets the user-facing product display name.
        /// </summary>
        public const string ApplicationDisplayName = "Foundry OSD";

        /// <summary>
        /// Gets the default update channel used by the update service.
        /// </summary>
        public const string DefaultUpdateChannel = "stable";

        public static readonly string RootDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ApplicationName);

        public static readonly string UserRootDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationName);

        public static readonly string SettingsDirectoryPath = Path.Combine(RootDirectoryPath, "Settings");
        public static readonly string LogDirectoryPath = Path.Combine(RootDirectoryPath, "Logs");
        public static readonly string CacheDirectoryPath = Path.Combine(RootDirectoryPath, "Cache");
        public static readonly string InstallerCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "Installers");
        public static readonly string WindowsSourceCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "WindowsSources");
        public static readonly string WinPeDriverCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "WinPeDrivers");
        public static readonly string WorkspacesDirectoryPath = Path.Combine(RootDirectoryPath, "Workspaces");
        public static readonly string ConfigurationWorkspaceDirectoryPath = Path.Combine(UserRootDirectoryPath, "Configuration");
        public static readonly string DeploymentProfilesDirectoryPath = Path.Combine(UserRootDirectoryPath, "Profiles");
        public static readonly string LegacyFoundryConfigurationStatePath = Path.Combine(WorkspacesDirectoryPath, "Configuration", "foundry.config.json");
        public static readonly string LegacyDefaultIsoPath = Path.Combine(WorkspacesDirectoryPath, "Iso", "Foundry.iso");
        public static readonly string IsoArtifactDirectoryPath = Path.Combine(RootDirectoryPath, "Artifacts", "Iso");
        public static readonly string DefaultIsoPath = Path.Combine(IsoArtifactDirectoryPath, "Foundry.iso");
        public static readonly string TempDirectoryPath = Path.Combine(RootDirectoryPath, "Temp");
        public static readonly string UsbQueryTempDirectoryPath = Path.Combine(TempDirectoryPath, "UsbQuery");
        public static readonly string WinReTempDirectoryPath = Path.Combine(TempDirectoryPath, "WinRe");
        public static readonly string WebView2UserDataDirectoryPath = Path.Combine(UserRootDirectoryPath, "WebView2");
        public static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "Foundry.log");
        public static readonly string AppSettingsPath = Path.Combine(SettingsDirectoryPath, "appsettings.json");
        public static readonly string FoundryConfigurationStatePath = Path.Combine(ConfigurationWorkspaceDirectoryPath, "foundry.config.json");

        public const string RepositoryUrl = "https://github.com/foundry-osd/foundry";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
        public const string DefaultUpdateFeedUrl = RepositoryUrl;

        /// <summary>
        /// Creates only startup state/log directories; operation outputs and caches are created on demand.
        /// </summary>
        public static void EnsureDataDirectories()
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            Directory.CreateDirectory(LogDirectoryPath);
            Directory.CreateDirectory(ConfigurationWorkspaceDirectoryPath);
        }
    }
}

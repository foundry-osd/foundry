namespace Foundry.Common
{
    public static partial class Constants
    {
        public const string ApplicationName = "Foundry";
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
        public static readonly string OperatingSystemCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "OperatingSystems");
        public static readonly string ToolCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "Tools");
        public static readonly string WorkspacesDirectoryPath = Path.Combine(RootDirectoryPath, "Workspaces");
        public static readonly string ConfigurationWorkspaceDirectoryPath = Path.Combine(WorkspacesDirectoryPath, "Configuration");
        public static readonly string WinPeWorkspaceDirectoryPath = Path.Combine(WorkspacesDirectoryPath, "WinPe");
        public static readonly string IsoWorkspaceDirectoryPath = Path.Combine(WorkspacesDirectoryPath, "Iso");
        public static readonly string TempDirectoryPath = Path.Combine(RootDirectoryPath, "Temp");
        public static readonly string UsbQueryTempDirectoryPath = Path.Combine(TempDirectoryPath, "UsbQuery");
        public static readonly string WinReTempDirectoryPath = Path.Combine(TempDirectoryPath, "WinRe");
        public static readonly string DownloadsTempDirectoryPath = Path.Combine(TempDirectoryPath, "Downloads");
        public static readonly string WebView2UserDataDirectoryPath = Path.Combine(UserRootDirectoryPath, "WebView2");
        public static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "Foundry.log");
        public static readonly string AppSettingsPath = Path.Combine(SettingsDirectoryPath, "appsettings.json");
        public static readonly string ExpertDeployConfigurationStatePath = Path.Combine(ConfigurationWorkspaceDirectoryPath, "foundry.expert.config.json");

        public const string RepositoryUrl = "https://github.com/foundry-osd/foundry";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
        public const string DefaultUpdateFeedUrl = RepositoryUrl;

        public static void EnsureDataDirectories()
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            Directory.CreateDirectory(LogDirectoryPath);
            Directory.CreateDirectory(CacheDirectoryPath);
            Directory.CreateDirectory(InstallerCacheDirectoryPath);
            Directory.CreateDirectory(OperatingSystemCacheDirectoryPath);
            Directory.CreateDirectory(ToolCacheDirectoryPath);
            Directory.CreateDirectory(WorkspacesDirectoryPath);
            Directory.CreateDirectory(ConfigurationWorkspaceDirectoryPath);
            Directory.CreateDirectory(WinPeWorkspaceDirectoryPath);
            Directory.CreateDirectory(IsoWorkspaceDirectoryPath);
            Directory.CreateDirectory(TempDirectoryPath);
            Directory.CreateDirectory(UsbQueryTempDirectoryPath);
            Directory.CreateDirectory(WinReTempDirectoryPath);
            Directory.CreateDirectory(DownloadsTempDirectoryPath);
            Directory.CreateDirectory(WebView2UserDataDirectoryPath);
        }
    }
}

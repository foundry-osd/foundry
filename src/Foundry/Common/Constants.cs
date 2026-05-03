namespace Foundry.Common
{
    public static partial class Constants
    {
        public const string ApplicationName = "Foundry";
        public const string DefaultUpdateChannel = "stable";

        public static readonly string RootDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ApplicationName);

        public static readonly string SettingsDirectoryPath = Path.Combine(RootDirectoryPath, "Settings");
        public static readonly string LogDirectoryPath = Path.Combine(RootDirectoryPath, "Logs");
        public static readonly string CacheDirectoryPath = Path.Combine(RootDirectoryPath, "Cache");
        public static readonly string InstallerCacheDirectoryPath = Path.Combine(CacheDirectoryPath, "Installers");
        public static readonly string WorkspacesDirectoryPath = Path.Combine(RootDirectoryPath, "Workspaces");
        public static readonly string TempDirectoryPath = Path.Combine(RootDirectoryPath, "Temp");
        public static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "Foundry.log");
        public static readonly string AppSettingsPath = Path.Combine(SettingsDirectoryPath, "appsettings.json");

        public const string RepositoryUrl = "https://github.com/foundry-osd/foundry";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
        public const string DefaultUpdateFeedUrl = RepositoryUrl;

        public static void EnsureDataDirectories()
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            Directory.CreateDirectory(LogDirectoryPath);
            Directory.CreateDirectory(CacheDirectoryPath);
            Directory.CreateDirectory(InstallerCacheDirectoryPath);
            Directory.CreateDirectory(WorkspacesDirectoryPath);
            Directory.CreateDirectory(TempDirectoryPath);
        }
    }
}

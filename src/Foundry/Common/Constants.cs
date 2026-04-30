namespace Foundry.Common
{
    public static partial class Constants
    {
        public static readonly string RootDirectoryPath = Path.Combine(PathHelper.GetAppDataFolderPath(), ProcessInfoHelper.ProductNameAndVersion);
        public static readonly string LogDirectoryPath = Path.Combine(RootDirectoryPath, "Log");
        public static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "Log.txt");
        public static readonly string AppConfigPath = Path.Combine(RootDirectoryPath, "AppConfig.json");
    }
}

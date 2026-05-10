namespace Foundry.Core.Services.WinPe;

public interface IWinPeEmbeddedAssetService
{
    string GetBootstrapScriptContent();
    string GetIanaWindowsTimeZoneMapJson();
    string GetSevenZipSourceDirectoryPath();
}

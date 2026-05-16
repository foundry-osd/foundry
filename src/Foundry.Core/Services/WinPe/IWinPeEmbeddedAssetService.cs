namespace Foundry.Core.Services.WinPe;

public interface IWinPeEmbeddedAssetService
{
    string GetBootstrapScriptContent();
    string GetUsbProvisioningScriptTemplateContent();
    string GetIanaWindowsTimeZoneMapJson();
    string GetSevenZipSourceDirectoryPath();
}

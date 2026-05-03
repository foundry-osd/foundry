namespace Foundry.Core.Services.Adk;

public interface IAdkInstallationProbe
{
    string? GetKitsRootPath();
    bool DirectoryExists(string path);
    bool FileExists(string path);
    bool DirectoryContainsFile(string directoryPath, string fileName);
    IReadOnlyList<AdkInstalledProduct> GetInstalledProducts();
}

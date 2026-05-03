namespace Foundry.Core.Services.Adk;

public interface IAdkInstallationProbe
{
    string? GetKitsRootPath();
    bool DirectoryExists(string path);
    IReadOnlyList<AdkInstalledProduct> GetInstalledProducts();
}

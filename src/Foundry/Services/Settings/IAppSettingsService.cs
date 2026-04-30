namespace Foundry.Services.Settings;

public interface IAppSettingsService
{
    FoundryAppSettings Current { get; }
    void Save();
}

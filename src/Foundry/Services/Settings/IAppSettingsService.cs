namespace Foundry.Services.Settings;

public interface IAppSettingsService
{
    FoundryAppSettings Current { get; }
    bool IsFirstRun { get; }
    void Save();
}

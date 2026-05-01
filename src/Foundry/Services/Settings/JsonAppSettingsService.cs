using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Foundry.Services.Settings;

internal sealed partial class JsonAppSettingsService : IAppSettingsService
{
    private readonly ILogger logger;

    public JsonAppSettingsService(ILogger logger)
    {
        this.logger = logger.ForContext<JsonAppSettingsService>();
        IsFirstRun = !File.Exists(Constants.AppSettingsPath);
        Current = Load();
        Save();
    }

    public FoundryAppSettings Current { get; }
    public bool IsFirstRun { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Constants.SettingsDirectoryPath);
            string json = JsonSerializer.Serialize(Current, FoundryAppSettingsJsonContext.Default.FoundryAppSettings);
            File.WriteAllText(Constants.AppSettingsPath, json);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to save app settings. SettingsPath={SettingsPath}", Constants.AppSettingsPath);
            throw;
        }
    }

    private FoundryAppSettings Load()
    {
        if (!File.Exists(Constants.AppSettingsPath))
        {
            return new FoundryAppSettings();
        }

        try
        {
            string json = File.ReadAllText(Constants.AppSettingsPath);
            return JsonSerializer.Deserialize(json, FoundryAppSettingsJsonContext.Default.FoundryAppSettings) ?? new FoundryAppSettings();
        }
        catch (Exception ex)
        {
            string backupPath = Constants.AppSettingsPath + ".invalid";
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(Constants.AppSettingsPath, backupPath);
                logger.Warning(
                    ex,
                    "App settings file was invalid and defaults were restored. SettingsPath={SettingsPath}, BackupPath={BackupPath}",
                    Constants.AppSettingsPath,
                    backupPath);
            }
            catch (Exception backupException)
            {
                logger.Error(
                    backupException,
                    "Failed to back up invalid app settings. SettingsPath={SettingsPath}, BackupPath={BackupPath}, OriginalError={OriginalError}",
                    Constants.AppSettingsPath,
                    backupPath,
                    ex.Message);
                throw;
            }

            return new FoundryAppSettings();
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(FoundryAppSettings))]
    private sealed partial class FoundryAppSettingsJsonContext : JsonSerializerContext;
}

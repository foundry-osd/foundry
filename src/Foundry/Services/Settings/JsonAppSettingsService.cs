using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Services.Settings;

internal sealed partial class JsonAppSettingsService : IAppSettingsService
{
    public JsonAppSettingsService()
    {
        Current = Load();
        Save();
    }

    public FoundryAppSettings Current { get; }

    public void Save()
    {
        Directory.CreateDirectory(Constants.SettingsDirectoryPath);
        string json = JsonSerializer.Serialize(Current, FoundryAppSettingsJsonContext.Default.FoundryAppSettings);
        File.WriteAllText(Constants.AppSettingsPath, json);
    }

    private static FoundryAppSettings Load()
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
        catch
        {
            string backupPath = Constants.AppSettingsPath + ".invalid";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(Constants.AppSettingsPath, backupPath);
            return new FoundryAppSettings();
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(FoundryAppSettings))]
    private sealed partial class FoundryAppSettingsJsonContext : JsonSerializerContext;
}

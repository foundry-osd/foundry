using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Foundry.Services.Settings;

/// <summary>
/// Persists application settings as JSON under the Foundry settings directory.
/// </summary>
/// <remarks>
/// Invalid settings files are moved aside with an <c>.invalid</c> suffix and replaced by defaults.
/// </remarks>
internal sealed partial class JsonAppSettingsService : IAppSettingsService
{
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonAppSettingsService"/> class.
    /// </summary>
    /// <param name="logger">Logger used for persistence and recovery diagnostics.</param>
    public JsonAppSettingsService(ILogger logger)
    {
        this.logger = logger.ForContext<JsonAppSettingsService>();
        IsFirstRun = !File.Exists(Constants.AppSettingsPath);
        Current = Load();
        EnsureTelemetryInstallId(Current);
        Save();
    }

    /// <inheritdoc />
    public FoundryAppSettings Current { get; }

    /// <inheritdoc />
    public bool IsFirstRun { get; }

    /// <inheritdoc />
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

    private static void EnsureTelemetryInstallId(FoundryAppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Telemetry.InstallId))
        {
            settings.Telemetry.InstallId = Guid.NewGuid().ToString("D");
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(FoundryAppSettings))]
    private sealed partial class FoundryAppSettingsJsonContext : JsonSerializerContext;
}

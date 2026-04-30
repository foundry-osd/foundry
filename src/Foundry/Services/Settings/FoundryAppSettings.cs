namespace Foundry.Services.Settings;

public sealed class FoundryAppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public AppearanceSettings Appearance { get; set; } = new();
    public LocalizationSettings Localization { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "system";
}

public sealed class LocalizationSettings
{
    public string Language { get; set; } = "en-US";
}

public sealed class UpdateSettings
{
    public bool CheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = Constants.DefaultUpdateChannel;
    public string FeedUrl { get; set; } = Constants.DefaultUpdateFeedUrl;
}

public sealed class DiagnosticsSettings
{
    public bool DeveloperMode { get; set; }
}

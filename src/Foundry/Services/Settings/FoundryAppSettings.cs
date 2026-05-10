namespace Foundry.Services.Settings;

public sealed class FoundryAppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public AppearanceSettings Appearance { get; set; } = new();
    public LocalizationSettings Localization { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
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

public sealed class MediaSettings
{
    public string IsoOutputPath { get; set; } = Path.Combine(Constants.IsoWorkspaceDirectoryPath, "Foundry.iso");
    public string Architecture { get; set; } = "X64";
    public bool UseCa2023Signature { get; set; }
    public string UsbPartitionStyle { get; set; } = "Gpt";
    public string UsbFormatMode { get; set; } = "Quick";
    public bool IncludeDellDrivers { get; set; }
    public bool IncludeHpDrivers { get; set; }
    public string? CustomDriverDirectoryPath { get; set; }
    public string WinPeLanguage { get; set; } = string.Empty;
}

public sealed class UpdateSettings
{
    public bool CheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = Constants.DefaultUpdateChannel;
    public string FeedUrl { get; set; } = Constants.DefaultUpdateFeedUrl;
    public DateTimeOffset? LastCheckedAt { get; set; }
}

public sealed class DiagnosticsSettings
{
    public bool DeveloperMode { get; set; }
}

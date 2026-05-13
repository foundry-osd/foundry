namespace Foundry.Services.Settings;

/// <summary>
/// Root document persisted to the user application settings file.
/// </summary>
public sealed class FoundryAppSettings
{
    /// <summary>
    /// Gets or sets the persisted settings schema version used for future migrations.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets visual appearance preferences.
    /// </summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>
    /// Gets or sets application language preferences.
    /// </summary>
    public LocalizationSettings Localization { get; set; } = new();

    /// <summary>
    /// Gets or sets media creation defaults.
    /// </summary>
    public MediaSettings Media { get; set; } = new();

    /// <summary>
    /// Gets or sets application update preferences and check history.
    /// </summary>
    public UpdateSettings Updates { get; set; } = new();

    /// <summary>
    /// Gets or sets diagnostic and developer-mode preferences.
    /// </summary>
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    /// <summary>
    /// Gets or sets anonymous telemetry preferences and identity.
    /// </summary>
    public TelemetryAppSettings Telemetry { get; set; } = new();
}

/// <summary>
/// Stores visual appearance preferences for the WinUI shell.
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>
    /// Gets or sets the theme identifier, such as <c>system</c>, <c>light</c>, or <c>dark</c>.
    /// </summary>
    public string Theme { get; set; } = "system";
}

/// <summary>
/// Stores localization preferences for the application shell.
/// </summary>
public sealed class LocalizationSettings
{
    /// <summary>
    /// Gets or sets the culture code used by the application resource loader.
    /// </summary>
    public string Language { get; set; } = "en-US";
}

/// <summary>
/// Stores defaults used when creating WinPE boot media.
/// </summary>
public sealed class MediaSettings
{
    /// <summary>
    /// Gets or sets the default ISO output path.
    /// </summary>
    public string IsoOutputPath { get; set; } = Path.Combine(Constants.IsoWorkspaceDirectoryPath, "Foundry.iso");

    /// <summary>
    /// Gets or sets the selected WinPE architecture name.
    /// </summary>
    public string Architecture { get; set; } = "X64";

    /// <summary>
    /// Gets or sets a value indicating whether the WinPE image should use the CA 2023 boot signature.
    /// </summary>
    public bool UseCa2023Signature { get; set; }

    /// <summary>
    /// Gets or sets the USB partition style used for removable media creation.
    /// </summary>
    public string UsbPartitionStyle { get; set; } = "Gpt";

    /// <summary>
    /// Gets or sets the USB formatting mode used for removable media creation.
    /// </summary>
    public string UsbFormatMode { get; set; } = "Quick";

    /// <summary>
    /// Gets or sets a value indicating whether Dell WinPE drivers should be included.
    /// </summary>
    public bool IncludeDellDrivers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether HP WinPE drivers should be included.
    /// </summary>
    public bool IncludeHpDrivers { get; set; }

    /// <summary>
    /// Gets or sets the optional custom driver directory included in WinPE media.
    /// </summary>
    public string? CustomDriverDirectoryPath { get; set; }

    /// <summary>
    /// Gets or sets the optional WinPE language code applied to generated media.
    /// </summary>
    public string WinPeLanguage { get; set; } = string.Empty;
}

/// <summary>
/// Stores application update preferences and the last successful check timestamp.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether update checks run during application startup.
    /// </summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets the update channel name passed to the update feed.
    /// </summary>
    public string Channel { get; set; } = Constants.DefaultUpdateChannel;

    /// <summary>
    /// Gets or sets the update feed URL.
    /// </summary>
    public string FeedUrl { get; set; } = Constants.DefaultUpdateFeedUrl;

    /// <summary>
    /// Gets or sets the last time an update check completed.
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; set; }
}

/// <summary>
/// Stores diagnostic preferences that affect logging and developer-only UI behavior.
/// </summary>
public sealed class DiagnosticsSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether developer diagnostics are enabled.
    /// </summary>
    public bool DeveloperMode { get; set; }
}

/// <summary>
/// Stores anonymous telemetry preference and the random installation identifier.
/// </summary>
public sealed class TelemetryAppSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether anonymous product telemetry is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the random anonymous installation identifier used for telemetry.
    /// </summary>
    public string InstallId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last local date where Foundry OSD activity telemetry was recorded.
    /// </summary>
    public string? LastDailyActiveDate { get; set; }
}

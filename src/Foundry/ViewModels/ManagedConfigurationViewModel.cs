// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// The read-only state every configuration page binds to (spec 6.2): while a followed publication manages the configuration,
/// editing controls of published sections are disabled and a banner names the reference. Per-PC values stay editable, and so do
/// passwords when the publication does not include them (they live in the local vault only).
/// </summary>
/// <remarks>One instance for the application; it follows the configuration state, the sharing service and the language.</remarks>
public sealed partial class ManagedConfigurationViewModel : ObservableObject
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly ConfigurationSharingService sharing;
    private readonly IApplicationLocalizationService localization;

    public ManagedConfigurationViewModel(
        IFoundryConfigurationStateService configuration,
        ConfigurationSharingService sharing,
        IApplicationLocalizationService localization)
    {
        this.configuration = configuration;
        this.sharing = sharing;
        this.localization = localization;
        configuration.ManagedChanged += OnChanged;
        sharing.Changed += OnChanged;
        localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets whether a followed publication manages the configuration.</summary>
    public bool IsManaged => configuration.IsManaged;

    /// <summary>Gets whether published sections can be edited: the configuration is not managed.</summary>
    public bool IsEditable => !configuration.IsManaged;

    /// <summary>Gets whether passwords can be entered: not managed, or the applied publication does not include passwords.</summary>
    public bool ArePasswordsEditable => !configuration.IsManaged || !(sharing.IsAvailable && sharing.Engine.State.IncludePasswords);

    /// <summary>Gets the banner text naming the reference PC, with the password rule.</summary>
    public string BannerText => !IsManaged ? string.Empty
        : localization.FormatString(ArePasswordsEditable ? "Sharing.ManagedBannerPasswordsEditable" : "Sharing.ManagedBanner", configuration.ManagedBy ?? string.Empty);

    /// <summary>Gets the banner title.</summary>
    public string BannerTitle => localization.GetString("Sharing.ManagedBannerTitle");

    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);
}

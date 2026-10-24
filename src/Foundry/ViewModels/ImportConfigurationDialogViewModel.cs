// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Globalization;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>One item the imported configuration needs that this PC lacks.</summary>
/// <param name="KindText">Localized kind of the content.</param>
/// <param name="Name">Display name of the content.</param>
public sealed record MissingContentItem(string KindText, string Name);

/// <summary>
/// Drives the import dialog: open a file with its passphrase or a previous configuration of this PC, show the preview with the
/// content this PC lacks, then replace the configuration. Failures stay inline.
/// </summary>
public sealed partial class ImportConfigurationDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationTransferService transfer;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private ConfigurationImportPreview? opened;
    private bool disposed;

    public ImportConfigurationDialogViewModel(ConfigurationTransferService transfer, IFilePickerService picker, IApplicationLocalizationService localization)
    {
        this.transfer = transfer;
        this.picker = picker;
        this.localization = localization;
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenFile))]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenFile))]
    public partial string Passphrase { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanOpenFile), nameof(CanImport), nameof(CanOpenLegacyProfile))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenLegacyProfile))]
    public partial LegacyLocalProfile? SelectedLegacyProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(CanImport), nameof(PreviewName), nameof(PreviewCreated), nameof(HasPreviewCreated),
        nameof(PreviewPasswords), nameof(IsLegacyConnectionFile), nameof(HasMissingContent))]
    public partial ImportPreview? Preview { get; set; }

    /// <summary>Gets the previous configurations of the legacy profile store of this Windows user.</summary>
    public ObservableCollection<LegacyLocalProfile> LegacyProfiles { get; } = [];

    /// <summary>Gets the content the previewed configuration needs that this PC lacks.</summary>
    public ObservableCollection<MissingContentItem> MissingContent { get; } = [];

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanOpenFile => CanEdit && FilePath.Length > 0 && Passphrase.Length > 0;
    public bool CanOpenLegacyProfile => CanEdit && SelectedLegacyProfile is not null;
    public bool CanImport => CanEdit && Preview is not null;
    public bool HasPreview => Preview is not null;
    public bool HasLegacyProfiles => LegacyProfiles.Count > 0;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasMissingContent => MissingContent.Count > 0;
    public bool IsLegacyConnectionFile => Preview?.IsLegacyConnectionFile == true;
    public string PreviewName => Preview?.Name ?? string.Empty;
    public bool HasPreviewCreated => Preview is { CreatedUtc: var created } && created != default;
    public string PreviewCreated => HasPreviewCreated ? Preview!.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture) : string.Empty;
    public string PreviewPasswords => Preview?.IncludesPasswords == true ? Text("PasswordsIncluded") : Text("PasswordsNotIncluded");

    public string Title => Text("ImportTitle");
    public string ImportLabel => Text("ImportButton");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string BrowseLabel => localization.GetString("Common.Browse");
    public string FileLabel => Text("FileLabel");
    public string PassphraseLabel => Text("PassphraseLabel");
    public string OpenLabel => Text("OpenButton");
    public string PreviousConfigurationsLabel => Text("PreviousConfigurations");
    public string NameLabel => Text("PreviewName");
    public string CreatedLabel => Text("PreviewCreated");
    public string PasswordsLabel => Text("PreviewPasswords");
    public string MissingContentHeader => Text("MissingContentHeader");
    public string MissingContentGuidance => Text("MissingContentGuidance");
    public string ReplaceWarning => Text("ReplaceWarning");
    public string LegacySyncRetiredText => Text("MigrationLegacySyncRetired");

    /// <summary>Gets the result of the completed import, or <see langword="null"/> when nothing was imported.</summary>
    public ConfigurationImportResult? Result { get; private set; }

    /// <summary>Loads the previous configurations of this PC; call once when the dialog opens.</summary>
    public async Task InitializeAsync()
    {
        IReadOnlyList<LegacyLocalProfile> profiles = await transfer.ListLegacyProfilesAsync();
        foreach (LegacyLocalProfile profile in profiles) LegacyProfiles.Add(profile);
        OnPropertyChanged(nameof(HasLegacyProfiles));
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (!CanEdit) return;
        try
        {
            string? path = await picker.PickOpenFileAsync(new FileOpenPickerRequest(
                Title, new string[] { ConfigurationTransfer.FileExtension, ConfigurationTransfer.LegacyFileExtension }, InitialFileTypeIndex: 0));
            if (path is null || !CanEdit) return;
            FilePath = path;
            ClearPreview();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Configuration import file selection failed.");
            ErrorMessage = Text("UnreadableFile");
        }
    }

    [RelayCommand]
    private Task OpenFileAsync() => CanOpenFile ? OpenAsync(() => transfer.PreviewFileAsync(FilePath, Passphrase)) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenLegacyProfileAsync() =>
        SelectedLegacyProfile is { } profile && CanEdit ? OpenAsync(() => transfer.PreviewLegacyProfileAsync(profile.LocalId)) : Task.CompletedTask;

    /// <summary>Replaces the configuration with the preview; returns true when it was imported.</summary>
    public async Task<bool> ImportAsync()
    {
        if (!CanImport) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            Result = await transfer.ImportAsync(Preview!);
            return true;
        }
        catch (InvalidOperationException exception) when (exception.Message == ConfigurationTransferService.FollowingRefusal)
        {
            // This PC started following a folder while the dialog was open.
            ErrorMessage = Text("ImportRefusedFollowing");
            return false;
        }
        catch (Exception exception)
        {
            // Any failure stays inline: this runs from the dialog button handler, which must never throw.
            Logger.Warning(exception, "Configuration import could not be completed from the dialog.");
            ErrorMessage = Text("ImportFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenAsync(Func<Task<ConfigurationImportPreview>> open)
    {
        ErrorMessage = string.Empty;
        ClearPreview();
        IsBusy = true;
        try
        {
            ConfigurationImportPreview result = await open();
            if (disposed) return;
            if (result.Preview is null)
            {
                ErrorMessage = localization.GetString(ConfigurationSharingViewModel.StatusKey(result.Status));
                return;
            }

            opened = result;
            ShowMissingContent();
            Preview = result.Preview;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Configuration could not be opened for import.");
            if (!disposed) ErrorMessage = Text("InvalidFile");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearPreview()
    {
        opened = null;
        Preview = null;
        MissingContent.Clear();
        OnPropertyChanged(nameof(HasMissingContent));
    }

    private void ShowMissingContent()
    {
        MissingContent.Clear();
        foreach (PackageContent content in opened?.MissingContent ?? []) MissingContent.Add(new MissingContentItem(KindText(content.Kind), content.Name));
        OnPropertyChanged(nameof(HasMissingContent));
    }

    private string KindText(SharedContentKind kind) => Text(kind switch
    {
        SharedContentKind.Package => "ContentKindPackage",
        SharedContentKind.DriverSet => "ContentKindDriverSet",
        SharedContentKind.Image => "ContentKindImage",
        _ => "ContentKindFile"
    });

    private string Text(string key) => localization.GetString("Sharing." + key);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        ShowMissingContent();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Passphrase = string.Empty;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Images;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Manages the local image library and the active profile's independent inclusion and defaults.</summary>
public sealed partial class CustomImagesViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService state;
    private readonly CustomImageLibraryService library;
    private readonly CustomImageImportDialogService importDialog;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private IReadOnlyList<CustomImageReference> localImages = [];
    private bool applying;
    private bool disposed;

    public CustomImagesViewModel(IFoundryConfigurationStateService state, CustomImageLibraryService library,
        CustomImageImportDialogService importDialog, IDialogService dialogs, IApplicationLocalizationService localization)
    {
        this.state = state;
        this.library = library;
        this.importDialog = importDialog;
        this.dialogs = dialogs;
        this.localization = localization;
        state.StateChanged += OnStateChanged;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyState();
    }

    public ObservableCollection<CustomImageRow> Images { get; } = [];
    public ObservableCollection<CustomImageIndexRow> Indexes { get; } = [];
    public ObservableCollection<string> SourceOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(CanClearDefault))]
    [NotifyPropertyChangedFor(nameof(CanSetIndexDefault))]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial int DefaultSourceIndex { get; set; }
    [ObservableProperty] public partial CustomImageRow? SelectedImage { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexDefault))]
    public partial CustomImageIndexRow? SelectedIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    public partial string RenameText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(CanClearDefault))]
    [NotifyPropertyChangedFor(nameof(CanSetIndexDefault))]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    public partial bool IsBusy { get; set; }

    public bool CanToggle => !IsBusy && !disposed;
    public bool CanAct => IsEnabled && CanToggle;
    public bool HasSelection => SelectedImage is not null;
    public bool HasImages => Images.Count > 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanEdit => CanAct && HasSelection;
    public bool CanSetIndexDefault => CanEdit && SelectedIndex is not null && Indexes.Contains(SelectedIndex);
    public bool CanRename => CanEdit && RenameText.Trim().Length is > 0 and <= CustomImageSettingsValidator.MaximumDisplayNameLength && !RenameText.Any(char.IsControl);
    public bool CanRemove => CanEdit;
    public bool CanClearDefault => CanAct && state.Current.CustomImages.DefaultImageId is not null;
    public bool HasReadinessIssue => !state.IsCustomImagesReady;
    public string ReadinessMessage => Text("ReadinessMessage");
    public bool IsEmpty => Images.Count == 0;
    public string EmptyMessage => Text("EmptyMessage");
    public string SelectedImageName => SelectedImage?.Name ?? string.Empty;
    public string ContentHash => SelectedImage is null ? string.Empty : "SHA256: " + SelectedImage.Reference.ContentHash;
    public string DocumentationUrl => FoundryApplicationInfo.DocumentationUrl + "/foundry-osd/customization/custom-windows-images";
    public string PageTitle => localization.GetString("Nav_CustomImagesKey.Title");
    public string PageDescription => localization.GetString("Nav_CustomImagesKey.Description");
    public string EnableLabel => Text("EnableLabel");
    public string ImportLabel => Text("ImportLabel");
    public string RefreshLabel => Text("RefreshLabel");
    public string NameLabel => Text("NameLabel");
    public string ArchitectureLabel => Text("ArchitectureLabel");
    public string IndexesLabel => Text("IndexesLabel");
    public string SizeLabel => Text("SizeLabel");
    public string IncludedLabel => Text("IncludedLabel");
    public string DefaultLabel => Text("DefaultLabel");
    public string StatusLabel => Text("StatusLabel");
    private bool IsSelectedImageIncluded => SelectedImage is { } row && state.Current.CustomImages.Images.Any(image => image.Id == row.Reference.Id && image.IsIncluded);
    public string IncludeLabel => Text(IsSelectedImageIncluded ? "ExcludeActionLabel" : "IncludeActionLabel");
    public Symbol IncludeSymbol => IsSelectedImageIncluded ? Symbol.Cancel : Symbol.Accept;
    public string RenameLabel => Text("RenameLabel");
    public string SetDefaultLabel => Text("SetDefaultLabel");
    public string ClearDefaultLabel => Text("ClearDefaultActionLabel");
    public string SetIndexDefaultLabel => Text("SetIndexDefaultLabel");
    public string IndexNumberLabel => Text("IndexNumberLabel");
    public string EditionLabel => Text("EditionLabel");
    public string VersionLabel => Text("VersionLabel");
    public string LanguagesLabel => Text("LanguagesLabel");
    public string SaveLabel => Text("SaveLabel");
    public string RemoveLabel => Text("RemoveLabel");
    public string CancelLabel => Text("CancelLabel");
    public string RemoveDescription => Text("RemoveDescription");
    public string DefaultSourceLabel => Text("DefaultSourceLabel");
    public string DefaultSourceDescription => Text("DefaultSourceDescription");
    public string IndexLabel => Text("IndexLabel");
    public string DetailsLabel => Text("DetailsLabel");
    public string LibraryLabel => Text("LibraryLabel");
    public string Text(string key) => localization.GetString("CustomImages." + key);

    partial void OnIsEnabledChanged(bool value)
    {
        if (!applying) state.UpdateCustomImages(state.Current.CustomImages with { IsEnabled = value });
    }

    partial void OnDefaultSourceIndexChanged(int value)
    {
        if (!applying && value is 0 or 1)
            state.UpdateCustomImages(state.Current.CustomImages with { DefaultSource = (CustomImageSource)value });
    }

    partial void OnSelectedImageChanged(CustomImageRow? value)
    {
        RenameText = value?.Reference.DisplayName ?? string.Empty;
        SelectedIndex = null;
        Indexes.Clear();
        foreach (CustomImageIndex index in value?.Reference.Indexes ?? [])
        {
            bool preferred = value?.Reference.Id == state.Current.CustomImages.DefaultImageId && index.Index == state.Current.CustomImages.DefaultImageIndex;
            Indexes.Add(new(index, preferred ? Text("Yes") : Text("No"), preferred));
        }
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanSetIndexDefault));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(SelectedImageName));
        OnPropertyChanged(nameof(ContentHash));
        OnPropertyChanged(nameof(IncludeLabel));
        OnPropertyChanged(nameof(IncludeSymbol));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || disposed) return;
        StatusMessage = string.Empty;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        try
        {
            var refreshed = (await library.ListAsync()).ToArray();
            for (int i = 0; i < refreshed.Length && !disposed; i++)
            {
                if (!refreshed[i].Indexes.Any(NeedsRevision) || !library.IsAvailable(refreshed[i])) continue;
                try { refreshed[i] = await library.RefreshMetadataAsync(refreshed[i]); }
                catch (Exception ex) { if (!disposed) ReportFailure(ex); }
            }
            if (disposed) return;
            localImages = refreshed;
            RefreshProfileMetadata();
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
    }

    private static bool NeedsRevision(CustomImageIndex index) =>
        Version.TryParse(index.Version, out Version? version) && version.Build >= 0 && version.Revision < 0;

    private void RefreshProfileMetadata()
    {
        CustomImagesSettings settings = state.Current.CustomImages;
        bool changed = false;
        CustomImageReference[] images = settings.Images.Select(image =>
        {
            CustomImageReference? local = localImages.FirstOrDefault(item => item.Length == image.Length &&
                item.ContentHash.Equals(image.ContentHash, StringComparison.OrdinalIgnoreCase));
            if (local is null || !image.Indexes.Any(NeedsRevision)) return image;
            bool imageChanged = false;
            CustomImageIndex[] indexes = image.Indexes.Select(index =>
            {
                if (!NeedsRevision(index)) return index;
                CustomImageIndex? refreshed = local.Indexes.FirstOrDefault(item => item.Index == index.Index);
                if (!Version.TryParse(refreshed?.Version, out Version? fullVersion) || fullVersion.Revision < 0 ||
                    !Version.TryParse(index.Version, out Version? version) || version.Major != fullVersion.Major ||
                    version.Minor != fullVersion.Minor || version.Build != fullVersion.Build) return index;
                imageChanged = true;
                return index with { Version = refreshed!.Version };
            }).ToArray();
            changed |= imageChanged;
            return imageChanged ? image with { Indexes = indexes } : image;
        }).ToArray();
        if (changed) state.UpdateCustomImages(settings with { Images = images });
        else state.RefreshCustomImageReadiness();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanAct) return;
        StatusMessage = string.Empty;
        IsBusy = true;
        CustomImagesSettings original = state.Current.CustomImages;
        try
        {
            CustomImageReference? imported = await importDialog.ShowAsync(original.Images.Select(image => image.DisplayName).ToArray());
            if (imported is not null && !disposed && ReferenceEquals(original, state.Current.CustomImages))
            {
                var existing = original.Images.FirstOrDefault(image => image.ContentHash.Equals(imported.ContentHash, StringComparison.OrdinalIgnoreCase));
                state.UpdateCustomImages(original with
                {
                    Images = existing is null ? [.. original.Images, imported] : original.Images.Select(image =>
                        image.Id == existing.Id ? image with { Indexes = imported.Indexes } : image).ToArray()
                });
            }
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
        await ReloadAsync();
    }

    [RelayCommand]
    private void ToggleIncluded()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        StatusMessage = string.Empty;
        CustomImagesSettings settings = state.Current.CustomImages;
        CustomImageReference? existing = settings.Images.FirstOrDefault(image => image.Id == row.Reference.Id);
        bool clearDefault = existing is { IsIncluded: true } && settings.DefaultImageId == existing.Id;
        state.UpdateCustomImages(settings with
        {
            Images = existing is null ? [.. settings.Images, row.Reference with { IsIncluded = true }] :
                settings.Images.Select(image => image.Id == existing.Id ? image with { IsIncluded = !image.IsIncluded } : image).ToArray(),
            DefaultImageId = clearDefault ? null : settings.DefaultImageId,
            DefaultImageIndex = clearDefault ? null : settings.DefaultImageIndex
        });
    }

    [RelayCommand]
    private void Rename()
    {
        if (!CanRename || SelectedImage is not { } row) return;
        try
        {
            string name = CustomImageSettingsValidator.NormalizeDisplayName(RenameText);
            if (state.Current.CustomImages.Images.Any(image => image.Id != row.Reference.Id && image.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = Text("DuplicateName");
                return;
            }
            StatusMessage = string.Empty;
            var settings = state.Current.CustomImages;
            state.UpdateCustomImages(settings with
            {
                Images = settings.Images.Any(image => image.Id == row.Reference.Id)
                ? settings.Images.Select(image => image.Id == row.Reference.Id ? image with { DisplayName = name } : image).ToArray()
                : [.. settings.Images, row.Reference with { DisplayName = name, IsIncluded = false }]
            });
        }
        catch (Exception ex) { ReportFailure(ex); }
    }

    [RelayCommand]
    private void SetDefault() => SetPreferredImage(null);

    [RelayCommand]
    private void SetIndexDefault()
    {
        if (CanSetIndexDefault) SetPreferredImage(SelectedIndex!.Index.Index);
    }

    private void SetPreferredImage(int? index)
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        StatusMessage = string.Empty;
        var settings = state.Current.CustomImages;
        var reference = row.Reference with { IsIncluded = true };
        var images = settings.Images.Any(image => image.Id == reference.Id)
            ? settings.Images.Select(image => image.Id == reference.Id ? reference : image).ToArray()
            : [.. settings.Images, reference];
        state.UpdateCustomImages(settings with { Images = images, DefaultImageId = reference.Id, DefaultImageIndex = index });
    }

    [RelayCommand]
    private void ClearDefault()
    {
        if (!CanClearDefault) return;
        StatusMessage = string.Empty;
        state.UpdateCustomImages(state.Current.CustomImages with { DefaultImageId = null, DefaultImageIndex = null });
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!CanRemove || SelectedImage is not { } row) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        CustomImagesSettings settings = state.Current.CustomImages;
        try
        {
            if (!await dialogs.ConfirmAsync(new ConfirmationDialogRequest(RemoveLabel,
                row.Name + " (" + row.Size + ")" + Environment.NewLine + RemoveDescription, RemoveLabel, CancelLabel)) ||
                disposed || !ReferenceEquals(settings, state.Current.CustomImages)) return;
            await library.DeleteAsync(row.Reference.ContentHash);
            if (!disposed && ReferenceEquals(settings, state.Current.CustomImages))
            {
                bool clearDefault = settings.DefaultImageId == row.Reference.Id;
                state.UpdateCustomImages(settings with
                {
                    Images = settings.Images.Where(image => image.Id != row.Reference.Id).ToArray(),
                    DefaultImageId = clearDefault ? null : settings.DefaultImageId,
                    DefaultImageIndex = clearDefault ? null : settings.DefaultImageIndex
                });
            }
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
        await ReloadAsync();
    }

    private void ApplyState()
    {
        if (disposed) return;
        applying = true;
        try
        {
            string? selectedId = SelectedImage?.Reference.Id;
            int? selectedIndex = SelectedIndex?.Index.Index;
            CustomImagesSettings settings = state.Current.CustomImages;
            IsEnabled = settings.IsEnabled;
            SourceOptions.Clear();
            SourceOptions.Add(Text("Catalog"));
            SourceOptions.Add(Text("Custom"));
            DefaultSourceIndex = (int)settings.DefaultSource;
            SelectedImage = null;
            Images.Clear();
            foreach (CustomImageReference image in settings.Images.Concat(localImages.Where(local =>
                !settings.Images.Any(profile => profile.ContentHash.Equals(local.ContentHash, StringComparison.OrdinalIgnoreCase)))))
            {
                bool inProfile = settings.Images.Any(profile => profile.Id == image.Id);
                bool available = library.IsAvailable(image);
                string[] architectures = image.Indexes.Select(index => index.Architecture).Distinct().ToArray();
                Images.Add(new(image, architectures.Length == 1 ? architectures[0] : Text("Multiple"),
                    image.Indexes.Count, $"{image.Length / 1073741824d:F2} GB", inProfile && image.IsIncluded ? Text("Yes") : Text("No"),
                    settings.DefaultImageId == image.Id ? Text("Yes") : Text("No"), available ? Text("Available") : Text("Missing"),
                    inProfile && image.IsIncluded, settings.DefaultImageId == image.Id, available));
            }
            SelectedImage = Images.FirstOrDefault(image => image.Reference.Id == selectedId);
            SelectedIndex = Indexes.FirstOrDefault(index => index.Index.Index == selectedIndex);
            OnPropertyChanged(nameof(HasReadinessIssue));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(CanClearDefault));
            OnPropertyChanged(nameof(CanRemove));
        }
        finally { applying = false; }
    }

    private void ReportFailure(Exception exception)
    {
        Logger.Warning(exception, "Custom image library operation failed.");
        StatusMessage = Text("OperationFailed");
    }

    private void OnStateChanged(object? sender, EventArgs e) => ApplyState();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        ApplyState();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        disposed = true;
        state.StateChanged -= OnStateChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

public sealed record CustomImageRow(CustomImageReference Reference, string Architecture, int Indexes, string Size,
    string Included, string Default, string Status, bool IsIncluded, bool IsDefault, bool IsAvailable)
{
    public string Name => Reference.DisplayName;
    public Style IncludedStyle => (Style)Application.Current.Resources[IsIncluded ? "FoundrySuccessTextBlockStyle" : "FoundrySecondaryTextBlockStyle"];
    public Style DefaultStyle => (Style)Application.Current.Resources[IsDefault ? "FoundrySuccessTextBlockStyle" : "FoundrySecondaryTextBlockStyle"];
    public Style StatusStyle => (Style)Application.Current.Resources[IsAvailable ? "FoundrySuccessTextBlockStyle" : "FoundryCriticalTextBlockStyle"];
}

/// <summary>Displays index metadata and the profile preference without changing the WIM.</summary>
public sealed record CustomImageIndexRow(CustomImageIndex Index, string Preferred, bool IsPreferred)
{
    public string Languages => string.Join(", ", Index.Languages);
    public Style PreferredStyle => (Style)Application.Current.Resources[IsPreferred ? "FoundrySuccessTextBlockStyle" : "FoundrySecondaryTextBlockStyle"];
}

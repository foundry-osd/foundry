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
    public ObservableCollection<CustomImageIndexOption> IndexOptions { get; } = [];
    public ObservableCollection<string> SourceOptions { get; } = [];

    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial int DefaultSourceIndex { get; set; }
    [ObservableProperty] public partial CustomImageRow? SelectedImage { get; set; }
    [ObservableProperty] public partial CustomImageIndexOption? SelectedIndex { get; set; }
    [ObservableProperty] public partial string RenameText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    public partial bool IsBusy { get; set; }

    public bool CanAct => !IsBusy;
    public bool CanEdit => !IsBusy && SelectedImage is not null;
    public bool HasReadinessIssue => !state.IsCustomImagesReady;
    public string ReadinessMessage => Text("ReadinessMessage");
    public bool IsEmpty => Images.Count == 0;
    public string EmptyMessage => Text("EmptyMessage");
    public string DefaultDescription => state.Current.CustomImages.DefaultImageId is null
        ? Text("OperatorChoice")
        : state.Current.CustomImages.Images.FirstOrDefault(image => image.Id == state.Current.CustomImages.DefaultImageId)?.DisplayName ?? Text("Missing");
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
    public string IncludeLabel => Text("IncludeLabel");
    public string RenameLabel => Text("RenameLabel");
    public string SetDefaultLabel => Text("SetDefaultLabel");
    public string ClearDefaultLabel => Text("ClearDefaultLabel");
    public string RemoveLabel => Text("RemoveLabel");
    public string DeleteLabel => Text("DeleteLabel");
    public string CancelLabel => Text("CancelLabel");
    public string DeleteDescription => Text("DeleteDescription");
    public string DefaultSourceLabel => Text("DefaultSourceLabel");
    public string IndexLabel => Text("IndexLabel");
    public string DetailsLabel => Text("DetailsLabel");
    public string LibraryDescription => Text("LibraryDescription");
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
        IndexOptions.Clear();
        IndexOptions.Add(new(null, Text("OperatorChoice")));
        foreach (CustomImageIndex index in value?.Reference.Indexes ?? [])
            IndexOptions.Add(new(index.Index, $"{index.Index}: {index.Name} ({index.Architecture})"));
        SelectedIndex = IndexOptions.FirstOrDefault(option => value?.Reference.Id == state.Current.CustomImages.DefaultImageId &&
            option.Index == state.Current.CustomImages.DefaultImageIndex) ?? IndexOptions[0];
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IndexDetails));
    }

    public string IndexDetails => (SelectedImage is null ? string.Empty : "SHA256: " + SelectedImage.Reference.ContentHash + Environment.NewLine) + string.Join(Environment.NewLine, (SelectedImage?.Reference.Indexes ?? []).Select(index =>
        $"{index.Index}: {index.Name} | {index.Architecture} | {index.EditionId} | {index.ProductType} | {index.Version} | {string.Join(", ", index.Languages)}"));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        try
        {
            localImages = await library.ListAsync();
            if (!disposed) state.RefreshCustomImageReadiness();
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy) return;
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
                    IsEnabled = original.IsEnabled || original.Images.Count == 0,
                    Images = existing is null ? [.. original.Images, imported] : original.Images.Select(image =>
                        image.Id == existing.Id ? image with { SourceBundleHash = imported.SourceBundleHash, Indexes = imported.Indexes } : image).ToArray()
                });
            }
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private void ToggleIncluded()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        CustomImagesSettings settings = state.Current.CustomImages;
        CustomImageReference? existing = settings.Images.FirstOrDefault(image => image.Id == row.Reference.Id);
        state.UpdateCustomImages(settings with
        {
            Images = existing is null ? [.. settings.Images, row.Reference with { IsIncluded = true }] :
                settings.Images.Select(image => image.Id == existing.Id ? image with { IsIncluded = !image.IsIncluded } : image).ToArray()
        });
    }

    [RelayCommand]
    private void Rename()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        try
        {
            string name = CustomImageSettingsValidator.NormalizeDisplayName(RenameText);
            if (state.Current.CustomImages.Images.Any(image => image.Id != row.Reference.Id && image.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = Text("DuplicateName");
                return;
            }
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
    private void SetDefault()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        var settings = state.Current.CustomImages;
        var reference = row.Reference with { IsIncluded = true };
        var images = settings.Images.Any(image => image.Id == reference.Id)
            ? settings.Images.Select(image => image.Id == reference.Id ? reference : image).ToArray()
            : [.. settings.Images, reference];
        state.UpdateCustomImages(settings with { Images = images, DefaultImageId = reference.Id, DefaultImageIndex = SelectedIndex?.Index });
    }

    [RelayCommand]
    private void ClearDefault()
    {
        if (!CanAct) return;
        state.UpdateCustomImages(state.Current.CustomImages with { DefaultImageId = null, DefaultImageIndex = null });
    }

    [RelayCommand]
    private void Remove()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        var settings = state.Current.CustomImages;
        state.UpdateCustomImages(settings with { Images = settings.Images.Where(image => image.Id != row.Reference.Id).ToArray() });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanEdit || SelectedImage is not { } row) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            if (await dialogs.ConfirmAsync(new ConfirmationDialogRequest(DeleteLabel, row.Name + " (" + row.Size + ")" + Environment.NewLine + DeleteDescription, DeleteLabel, CancelLabel)))
                await library.DeleteAsync(row.Reference.ContentHash);
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    private void ApplyState()
    {
        if (disposed) return;
        applying = true;
        try
        {
            string? selectedId = SelectedImage?.Reference.Id;
            CustomImagesSettings settings = state.Current.CustomImages;
            IsEnabled = settings.IsEnabled;
            SourceOptions.Clear();
            SourceOptions.Add(Text("Catalog"));
            SourceOptions.Add(Text("Custom"));
            DefaultSourceIndex = (int)settings.DefaultSource;
            Images.Clear();
            foreach (CustomImageReference image in settings.Images.Concat(localImages.Where(local =>
                !settings.Images.Any(profile => profile.ContentHash.Equals(local.ContentHash, StringComparison.OrdinalIgnoreCase)))))
            {
                bool inProfile = settings.Images.Any(profile => profile.Id == image.Id);
                bool available = library.IsAvailable(image);
                string[] architectures = image.Indexes.Select(index => index.Architecture).Distinct().ToArray();
                Images.Add(new(image, architectures.Length == 1 ? architectures[0] : Text("Multiple"),
                    image.Indexes.Count, $"{image.Length / 1073741824d:F2} GB", inProfile && image.IsIncluded ? Text("Yes") : Text("No"),
                    settings.DefaultImageId == image.Id ? Text("Yes") : Text("No"), available ? Text("Available") : Text("Missing")));
            }
            SelectedImage = Images.FirstOrDefault(image => image.Reference.Id == selectedId);
            OnPropertyChanged(nameof(DefaultDescription));
            OnPropertyChanged(nameof(HasReadinessIssue));
            OnPropertyChanged(nameof(IsEmpty));
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
    string Included, string Default, string Status)
{
    public string Name => Reference.DisplayName;
}

public sealed record CustomImageIndexOption(int? Index, string DisplayName);

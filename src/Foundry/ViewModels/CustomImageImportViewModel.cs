// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Images;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Keeps import progress and cancellation alive until the library has released its staging resources.</summary>
public sealed partial class CustomImageImportViewModel : ObservableObject, IDisposable
{
    private readonly CustomImageLibraryService library;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private readonly HashSet<string> existingNames;
    private CancellationTokenSource? cancellation;
    private CustomImageImportPreview? preview;
    private int sourceRevision;
    private bool disposed;

    public CustomImageImportViewModel(CustomImageLibraryService library, IFilePickerService picker,
        IApplicationLocalizationService localization, IReadOnlyList<string> existingNames)
    {
        this.library = library;
        this.picker = picker;
        this.localization = localization;
        this.existingNames = existingNames.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial string SourcePath { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(NameValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasNameValidation))]
    public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; set; }
    [ObservableProperty] public partial bool IsProcessing { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceChoices))]
    public partial IReadOnlyList<string> SourceChoices { get; set; } = [];
    [ObservableProperty] public partial string? SelectedIsoImagePath { get; set; }
    public bool HasSourceChoices => SourceChoices.Count > 1;
    public bool HasPreview => preview is not null;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasNameValidation => !string.IsNullOrEmpty(NameValidationMessage);
    public bool CanEdit => !IsBusy && !disposed;
    public bool CanImport => CanEdit && preview is not null && DisplayName.Trim().Length is > 0 and <= CustomImageSettingsValidator.MaximumDisplayNameLength &&
        !DisplayName.Any(char.IsControl) && !existingNames.Contains(DisplayName.Trim());
    public string NameValidationMessage => existingNames.Contains(DisplayName.Trim()) ? Text("DuplicateName") : string.Empty;
    public string PreviewSummary => preview is null ? string.Empty :
        $"{Text("SizeLabel")}: {preview.Length:N0} B\n{Text("IndexesLabel")}: {preview.Indexes.Count}\n" +
        string.Join("\n", preview.Indexes.Select(index =>
            $"{index.Index}: {index.Name} | {index.Architecture} | {index.Version} | {string.Join(", ", index.Languages)}"));
    public CustomImageReference? Result { get; private set; }
    public string Title => Text("ImportLabel");
    public string CancelLabel => Text("CancelLabel");
    public string BrowseLabel => Text("BrowseLabel");
    public string SourceLabel => Text("SourceLabel");
    public string NameLabel => Text("NameLabel");
    public string SourceChoiceMessage => Text("SourceChoiceRequired");
    private string Text(string key) => localization.GetString("CustomImages." + key);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (!CanEdit) return;
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        IsBusy = true;
        string? path;
        try
        {
            path = await picker.PickOpenFileAsync(new FileOpenPickerRequest(Title, (string[])[".iso", ".wim"]));
            if (path is null || operation.IsCancellationRequested || disposed) return;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Custom image source selection failed.");
            Status = Text("OperationFailed");
            return;
        }
        finally
        {
            IsBusy = false;
            cancellation = null;
        }
        SourcePath = path;
        if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = Path.GetFileNameWithoutExtension(path);
        await InspectAsync();
    }

    partial void OnSourcePathChanged(string value)
    {
        sourceRevision++;
        cancellation?.Cancel();
        ResetPreview();
        SourceChoices = [];
        SelectedIsoImagePath = null;
    }

    partial void OnSelectedIsoImagePathChanged(string? value)
    {
        sourceRevision++;
        ResetPreview();
        if (!string.IsNullOrEmpty(value) && CanEdit) _ = InspectAsync();
    }

    private void ResetPreview()
    {
        preview = null;
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(HasPreview));
    }

    private async Task InspectAsync()
    {
        if (!CanEdit || string.IsNullOrWhiteSpace(SourcePath)) return;
        ResetPreview();
        int revision = sourceRevision;
        string sourcePath = SourcePath;
        string? isoImagePath = SelectedIsoImagePath;
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        IsBusy = true;
        IsIndeterminate = true;
        Progress = 0;
        Status = Text("StageInspecting");
        IsProcessing = true;
        try
        {
            CustomImageImportPreview result = await library.PreviewAsync(sourcePath, isoImagePath, operation.Token);
            if (disposed || operation.IsCancellationRequested || revision != sourceRevision) return;
            preview = result;
            Status = string.Empty;
            OnPropertyChanged(nameof(PreviewSummary));
            OnPropertyChanged(nameof(HasPreview));
        }
        catch (CustomImageSourceChoiceRequiredException ex)
        {
            if (disposed || operation.IsCancellationRequested || revision != sourceRevision) return;
            SourceChoices = ex.Candidates.ToArray();
            Status = Text("SourceChoiceRequired");
        }
        catch (OperationCanceledException) { if (!disposed && revision == sourceRevision) Status = Text("Canceled"); }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Custom image preview failed.");
            if (!disposed && revision == sourceRevision) Status = Text("OperationFailed");
        }
        finally
        {
            IsProcessing = false;
            IsIndeterminate = false;
            cancellation = null;
            IsBusy = false;
        }
    }

    public async Task<bool> ImportAsync()
    {
        if (!CanImport) return false;
        IsBusy = true;
        cancellation = new CancellationTokenSource();
        Status = string.Empty;
        IsIndeterminate = true;
        Progress = 0;
        IsProcessing = true;
        try
        {
            var progress = new Progress<CustomImageImportProgress>(value =>
            {
                Status = Text("Stage" + value.Stage);
                IsIndeterminate = value.TotalBytes <= 0;
                Progress = value.TotalBytes > 0 ? 100d * value.CompletedBytes / value.TotalBytes : 0;
            });
            Result = await library.ImportAsync(new(SourcePath, DisplayName.Trim(), SelectedIsoImagePath), progress, cancellation.Token);
            return true;
        }
        catch (CustomImageSourceChoiceRequiredException ex)
        {
            ResetPreview();
            SelectedIsoImagePath = null;
            SourceChoices = ex.Candidates.ToArray();
            Status = Text("SourceChoiceRequired");
        }
        catch (OperationCanceledException) { Status = Text("Canceled"); }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Custom image import failed.");
            Status = Text("OperationFailed");
        }
        finally
        {
            IsProcessing = false;
            IsIndeterminate = false;
            IsBusy = false;
            cancellation.Dispose();
            cancellation = null;
        }
        return false;
    }

    public void Cancel() => cancellation?.Cancel();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);
    public void Dispose()
    {
        disposed = true;
        cancellation?.Cancel();
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

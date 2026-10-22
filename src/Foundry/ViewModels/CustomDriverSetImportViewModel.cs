// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the custom driver set import dialog: folder and name selection, the copy with progress, and inline failures.
/// Import failures and content already in the profile keep the dialog open; only a new set produces a <see cref="Result"/>.
/// </summary>
public sealed partial class CustomDriverSetImportViewModel : ObservableObject, IDisposable
{
    private readonly CustomDriverSetLibrary library;
    private readonly IFilePickerService picker;
    private readonly IFoundryConfigurationStateService state;
    private readonly IApplicationLocalizationService localization;
    private readonly HashSet<string> existingNames;
    private CancellationTokenSource? cancellation;
    private string suggestedName = string.Empty;
    private bool disposed;

    public CustomDriverSetImportViewModel(CustomDriverSetLibrary library, IFilePickerService picker, IFoundryConfigurationStateService state,
        IApplicationLocalizationService localization)
    {
        this.library = library;
        this.picker = picker;
        this.state = state;
        this.localization = localization;
        existingNames = state.Current.CustomDrivers.Sets.Select(set => set.DisplayName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial string SourcePath { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport), nameof(NameValidationMessage), nameof(HasNameValidation))]
    public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport), nameof(CanEdit))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double Progress { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Error;

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanImport => CanEdit && !string.IsNullOrEmpty(SourcePath) && DisplayName.Trim().Length is > 0 and <= CustomDriverSetsSettingsValidator.MaximumDisplayNameLength &&
        !DisplayName.Any(char.IsControl) && !existingNames.Contains(DisplayName.Trim());
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasNameValidation => !string.IsNullOrEmpty(NameValidationMessage);
    public string NameValidationMessage => existingNames.Contains(DisplayName.Trim()) ? Text("DuplicateName") : string.Empty;
    public int MaximumNameLength => CustomDriverSetsSettingsValidator.MaximumDisplayNameLength;
    public string ProgressText => localization.FormatString("CustomDrivers.ImportProgressFormat", (int)Math.Floor(Progress));
    public string Title => Text("ImportLabel");
    public string ImportLabel => Text("ImportButton");
    public string FolderLabel => Text("ImportFolderLabel");
    public string BrowseLabel => localization.GetString("CustomImages.BrowseLabel");
    public string NameLabel => localization.GetString("CustomImages.NameLabel");
    public string CancelLabel => localization.GetString("Common.Cancel");

    /// <summary>Gets the imported set to add to the profile, or <see langword="null"/> when nothing was imported.</summary>
    public CustomDriverSetImportResult? Result { get; private set; }

    /// <summary>Gets the id of the profile set that already holds the last imported content, so the page can select it.</summary>
    public string? ExistingSetId { get; private set; }

    private string Text(string key) => localization.GetString("CustomDrivers." + key);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (!CanEdit) return;
        string? path;
        try
        {
            path = await picker.PickFolderAsync(new FolderPickerRequest(Text("ImportPickerTitle")));
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Custom driver set folder selection failed.");
            ShowMessage(Text("ImportFailed"), InfoBarSeverity.Error);
            return;
        }
        if (path is null || !CanEdit) return;
        Message = string.Empty;
        SourcePath = path;
        // A name typed by the user survives a new folder choice; the previous suggestion does not.
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName == suggestedName)
        {
            suggestedName = CreateUniqueName(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            DisplayName = suggestedName;
        }
    }

    /// <summary>
    /// Copies the selected folder into the driver library with progress. The task completes only after the library has removed
    /// its pending content, so a dialog closed by <see cref="Cancel"/> never leaves a partial import behind.
    /// </summary>
    /// <returns><see langword="true"/> when <see cref="Result"/> holds a set to add.</returns>
    public async Task<bool> ImportAsync()
    {
        if (!CanImport) return false;
        string source = SourcePath;
        string name = DisplayName.Trim();
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        ExistingSetId = null;
        Message = string.Empty;
        Progress = 0;
        IsBusy = true;
        Logger.Information("Custom driver set import started.");
        try
        {
            // Created on the UI thread so reports from the library thread are marshalled back to it.
            var progress = new Progress<ContentPackageImportProgress>(value => OnProgress(operation, value));
            CancellationToken token = operation.Token;
            ContentPackageReference package = await Task.Run(() => library.ImportAsync(source, progress, token), token);
            int? infCount = await Task.Run(() => library.TryCountInfFiles(package.ContentHash), token);
            Logger.Information("Custom driver set import completed. ContentHash={ContentHash}, FileCount={FileCount}, Length={Length}, InfCount={InfCount}",
                package.ContentHash, package.FileCount, package.Length, infCount);
            // Re-importing content that was recorded as missing must clear that state without a manual refresh.
            await state.InspectCustomDriverSetAsync(package);
            // The copy can reach 100% before Cancel; a request made during the INF count or inspection must still add nothing.
            if (disposed || operation.IsCancellationRequested)
            {
                Logger.Information("Custom driver set import canceled.");
                return false;
            }
            CustomDriverSetReference? existing = state.Current.CustomDrivers.Sets.FirstOrDefault(set =>
                set.Package.ContentHash.Equals(package.ContentHash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Logger.Information("Custom driver set import not added because the profile already contains its content. ContentHash={ContentHash}", package.ContentHash);
                ExistingSetId = existing.Id;
                ShowMessage(localization.FormatString("CustomDrivers.AlreadyInProfile", existing.DisplayName), InfoBarSeverity.Informational);
                return false;
            }
            Result = new(package, name, infCount);
            return true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Logger.Information("Custom driver set import canceled.");
        }
        catch (Exception exception)
        {
            string? relativePath = exception.Data[ContentPackagePathPolicy.RelativePathDataKey] as string;
            Logger.Warning(exception, "Custom driver set import failed. ErrorCode={ErrorCode}, RelativePath={RelativePath}", exception.Message, relativePath);
            if (!disposed) ShowMessage(DescribeImportFailure(exception, relativePath), InfoBarSeverity.Error);
        }
        finally
        {
            cancellation = null;
            IsBusy = false;
        }
        return false;
    }

    /// <summary>Requests cancellation of the running import; <see cref="IsBusy"/> turns false once the library has cleaned up.</summary>
    public void Cancel() => cancellation?.Cancel();

    private void OnProgress(CancellationTokenSource operation, ContentPackageImportProgress value)
    {
        // Reports are posted asynchronously and can arrive after the import that produced them has ended.
        if (!ReferenceEquals(cancellation, operation)) return;
        Progress = value.TotalBytes > 0 ? 100d * value.CompletedBytes / value.TotalBytes : 100d * value.CompletedFiles / value.TotalFiles;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageSeverity = severity;
        Message = message;
    }

    /// <summary>Defaults the label to the folder name and appends a counter when the profile already uses it.</summary>
    private string CreateUniqueName(string folderName)
    {
        string baseName = string.IsNullOrWhiteSpace(folderName) ? Text("DefaultSetName") : folderName.Trim();
        string name = Truncate(baseName, MaximumNameLength);
        for (int counter = 2; existingNames.Contains(name); counter++)
        {
            string suffix = $" ({counter})";
            name = Truncate(baseName, MaximumNameLength - suffix.Length).TrimEnd() + suffix;
        }
        return name;
    }

    /// <summary>Cuts to <paramref name="length"/> UTF-16 units without splitting a surrogate pair.</summary>
    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        return char.IsHighSurrogate(value[length - 1]) ? value[..(length - 1)] : value[..length];
    }

    /// <summary>Maps library error codes to actionable messages; any other failure keeps the generic message.</summary>
    private string DescribeImportFailure(Exception exception, string? relativePath) => exception.Message switch
    {
        "ContentPackage.InvalidPackagePath" when relativePath is not null => localization.FormatString("CustomDrivers.ImportInvalidPath", relativePath),
        "ContentPackage.PackageTooManyFiles" => Text("ImportTooManyFiles"),
        "ContentPackage.MissingInfFile" or "ContentPackage.EmptyPackage" => Text("ImportMissingInf"),
        "ContentPackage.PackageSourceChanged" => Text("ImportSourceChanged"),
        "ContentPackage.InsufficientPackageSpace" => Text("ImportInsufficientSpace"),
        "ContentPackage.RedirectedPackagePath" => Text("ImportRedirected"),
        _ => Text("ImportFailed")
    };

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation?.Cancel();
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Carries a newly imported driver set and the label chosen for it in the import dialog.</summary>
/// <param name="Package">Library package produced by the import.</param>
/// <param name="Name">Trimmed, validated display name for the new set.</param>
/// <param name="InfCount">INF count read from the package manifest, or <see langword="null"/> when it could not be read.</param>
public sealed record CustomDriverSetImportResult(ContentPackageReference Package, string Name, int? InfCount);

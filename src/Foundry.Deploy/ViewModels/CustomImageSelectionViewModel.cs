// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Images;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Images;
using CustomImageSourceLease = Foundry.Deploy.Services.Images.CustomImageSourceLease;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.ViewModels;

/// <summary>Keeps custom source defaults separate from catalog choices and discards stale asynchronous inspection.</summary>
public partial class CustomImageSelectionViewModel : ObservableObject, IDisposable
{
    private readonly CustomImageCatalogService _catalog;
    private readonly ICustomImageMetadataReader _reader;
    private DeployCustomImagesSettings _settings = new();
    private CancellationTokenSource? _inspection;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _applyingCatalog;
    private bool _applyingIndexes;
    private bool _defaultsApplied;
    private int? _rememberedIndex;
    private int _configurationVersion;
    private CustomImageAsset? _inspectedAsset;

    public CustomImageSelectionViewModel(CustomImageCatalogService? catalog = null, ICustomImageMetadataReader? reader = null)
    {
        _catalog = catalog ?? new();
        _reader = reader ?? new NativeCustomImageMetadataReader();
    }

    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isCustom;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorKey = string.Empty;
    [ObservableProperty] private CustomImageAsset? selectedAsset;
    [ObservableProperty] private CustomImageIndex? selectedIndex;

    public ObservableCollection<CustomImageAsset> Images { get; } = [];
    public ObservableCollection<CustomImageIndex> Indexes { get; } = [];
    public bool IsCatalog { get => !IsCustom; set => IsCustom = !value; }
    public string ErrorMessage => string.IsNullOrWhiteSpace(ErrorKey) ? string.Empty : LocalizationText.GetString(ErrorKey);
    public CustomImageSelection? Selection => SelectedAsset is not null && SelectedIndex is not null && !IsBusy &&
        (_debugSnapshot is null || DebugSafetyMode.IsEnabled)
        ? new(_inspectedAsset ?? SelectedAsset, SelectedIndex) : null;
    public string MetadataText => SelectedIndex is null ? string.Empty :
        $"{SelectedIndex.EditionId} | {SelectedIndex.Architecture} | {SelectedIndex.Version ?? LocalizationText.GetString("Common.Unknown")} | {string.Join(", ", SelectedIndex.Languages)}";
    public event EventHandler? StateChanged;

    public void Configure(DeployCustomImagesSettings settings)
    {
        _configuredSettings = settings;
        _debugSnapshot = null;
        ConfigureSelection(settings);
        NotifyDebugScenario();
    }

    private void ConfigureSelection(DeployCustomImagesSettings settings)
    {
        _configurationVersion++;
        _inspection?.Cancel();
        _inspection?.Dispose();
        _inspection = null;
        _inspectedAsset = null;
        IsBusy = false;
        _rememberedIndex = null;
        _applyingCatalog = true;
        SelectedAsset = null;
        SelectedIndex = null;
        Images.Clear();
        Indexes.Clear();
        ErrorKey = string.Empty;
        _applyingCatalog = false;
        _settings = settings;
        _defaultsApplied = false;
        IsEnabled = settings.IsEnabled;
        IsCustom = settings.IsEnabled && settings.DefaultSource == CustomImageSource.Custom;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!IsEnabled) return;
        int version = _configurationVersion;
        IsBusy = true;
        try
        {
            CustomImageCatalogResult result = _debugSnapshot is { } debug
                ? GetDebugCatalog(debug)
                : await _catalog.DiscoverAsync(_settings, _lifetime.Token);
            if (version != _configurationVersion) return;
            ApplyCatalog(result.Images);
            if (ErrorKey.Length == 0) ErrorKey = result.ErrorKey;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (version == _configurationVersion) ErrorKey = "CustomImages.InvalidManifest"; }
        finally { if (version == _configurationVersion) IsBusy = false; }
        if (version == _configurationVersion && SelectedAsset is not null) await InspectAsync(SelectedAsset);
    }

    public void ApplyCatalog(IReadOnlyList<CustomImageAsset> images)
    {
        _inspection?.Cancel();
        _applyingCatalog = true;
        try
        {
            _inspectedAsset = null;
            CustomImageAsset? previous = SelectedAsset;
            _rememberedIndex = SelectedIndex?.Index;
            Images.Clear();
            foreach (CustomImageAsset image in images) Images.Add(image);
            ErrorKey = string.Empty;
            if (!_defaultsApplied && _settings.DefaultImageId is not null)
            {
                CustomImageAsset[] matches = images.Where(image => image.IsManaged && image.Id == _settings.DefaultImageId).ToArray();
                SelectedAsset = matches.Length == 1 ? matches[0] : null;
                if (SelectedAsset is null) ErrorKey = matches.Length > 1 ? "CustomImages.AmbiguousDefault" : "CustomImages.MissingDefault";
            }
            else SelectedAsset = images.FirstOrDefault(image => image.ImagePath == previous?.ImagePath && image.Id == previous.Id);
            SelectedIndex = null;
            Indexes.Clear();
        }
        finally { _applyingCatalog = false; }
    }

    public void ApplyIndexes(IReadOnlyList<CustomImageIndex> indexes)
    {
        _applyingIndexes = true;
        try
        {
            Indexes.Clear();
            foreach (CustomImageIndex index in indexes) Indexes.Add(index);
            if (indexes.Count == 0 || indexes.Any(index => index.Index < 1) || indexes.Select(index => index.Index).Distinct().Count() != indexes.Count)
            {
                SelectedIndex = null;
                ErrorKey = "CustomImages.InvalidSource";
                return;
            }
            if (!_defaultsApplied && SelectedAsset?.Id == _settings.DefaultImageId && _settings.DefaultImageIndex is int preferred)
            {
                SelectedIndex = indexes.SingleOrDefault(index => index.Index == preferred);
                if (SelectedIndex is null) ErrorKey = "CustomImages.MissingDefault";
            }
            else if (_rememberedIndex is int remembered)
            {
                SelectedIndex = indexes.SingleOrDefault(index => index.Index == remembered);
                if (SelectedIndex is null) ErrorKey = "CustomImages.InvalidSource";
            }
            else SelectedIndex = indexes.Count == 1 ? indexes[0] : null;
        }
        finally { _applyingIndexes = false; }
    }

    private async Task InspectAsync(CustomImageAsset asset)
    {
        _inspection?.Cancel();
        _inspection?.Dispose();
        var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _inspection = pending;
        IsBusy = true;
        ErrorKey = string.Empty;
        SelectedIndex = null;
        Indexes.Clear();
        try
        {
            if (_debugSnapshot is { } debug)
            {
                if (!DebugSafetyMode.IsEnabled) throw new InvalidOperationException("Debug image scenarios require an attached debugger in a Debug build.");
                await Task.Delay(350, pending.Token);
                if (pending.IsCancellationRequested || !ReferenceEquals(_inspection, pending)) return;
                if (debug.InspectionErrorKey.Length > 0) ErrorKey = debug.InspectionErrorKey;
                else
                {
                    _inspectedAsset = asset;
                    ApplyIndexes(debug.Indexes);
                }
                return;
            }
            using CustomImageSourceLease lease = await CustomImageSourceLease.AcquireAsync(asset.ImagePath, asset.ExpectedLength, asset.ExpectedHash, pending.Token);
            IReadOnlyList<CustomImageIndex> indexes = await _reader.ReadAsync(asset.ImagePath, pending.Token);
            if (!pending.IsCancellationRequested && ReferenceEquals(_inspection, pending))
            {
                _inspectedAsset = asset with { ObservedHash = lease.ContentHash, ObservedLength = lease.Length };
                ApplyIndexes(indexes);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (ReferenceEquals(_inspection, pending)) ErrorKey = "CustomImages.InvalidSource"; }
        finally { if (ReferenceEquals(_inspection, pending)) IsBusy = false; }
    }

    partial void OnSelectedAssetChanged(CustomImageAsset? value)
    {
        if (_applyingCatalog) return;
        _defaultsApplied = true;
        _rememberedIndex = null;
        _inspection?.Cancel();
        _inspectedAsset = null;
        SelectedIndex = null;
        Indexes.Clear();
        ErrorKey = string.Empty;
        if (value is not null) _ = InspectAsync(value);
        NotifySelection();
    }

    partial void OnSelectedIndexChanged(CustomImageIndex? value)
    {
        if (!_applyingIndexes && !_applyingCatalog && value is not null)
        {
            _defaultsApplied = true;
            ErrorKey = string.Empty;
        }
        NotifySelection();
    }
    partial void OnIsBusyChanged(bool value) => NotifySelection();
    partial void OnIsCustomChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCatalog));
        NotifySelection();
    }
    partial void OnErrorKeyChanged(string value) => OnPropertyChanged(nameof(ErrorMessage));
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(DebugScenarioText));
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(MetadataText));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        _lifetime.Cancel();
        _inspection?.Cancel();
        _inspection?.Dispose();
        _lifetime.Dispose();
    }
}

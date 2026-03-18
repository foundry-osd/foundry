using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DriverPackSelectionViewModel : ObservableObject
{
    private const string NoneDriverPackOptionKey = "none";
    private const string MicrosoftUpdateCatalogDriverPackOptionKey = "microsoft-update-catalog";
    private const string DellDriverPackOptionKey = "oem:dell";
    private const string LenovoDriverPackOptionKey = "oem:lenovo";
    private const string HpDriverPackOptionKey = "oem:hp";
    private const string MicrosoftOemDriverPackOptionKey = "oem:microsoft";

    private readonly IDriverPackSelectionService _driverPackSelectionService;
    private HardwareProfile? _detectedHardware;
    private OperatingSystemCatalogItem? _selectedOperatingSystem;
    private string _effectiveArchitecture;
    private bool _isUpdatingDriverPackOptionSelection;
    private bool _hasUserSelectedDriverPackOption;

    public DriverPackSelectionViewModel(IDriverPackSelectionService driverPackSelectionService)
        : this(driverPackSelectionService, string.Empty)
    {
    }

    public DriverPackSelectionViewModel(IDriverPackSelectionService driverPackSelectionService, string initialArchitecture)
    {
        _driverPackSelectionService = driverPackSelectionService ?? throw new ArgumentNullException(nameof(driverPackSelectionService));
        _effectiveArchitecture = NormalizeArchitecture(initialArchitecture);
    }

    public event EventHandler? StateChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriverPackModeDisplay))]
    [NotifyPropertyChangedFor(nameof(IsOemDriverSourceSelected))]
    [NotifyPropertyChangedFor(nameof(IsDriverPackModelSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDriverPackVersionSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    private DriverPackOptionItem? selectedDriverPackOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    private string selectedDriverPackModel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    private string selectedDriverPackVersion = string.Empty;

    [ObservableProperty]
    private bool autoSelectDriverPackWhenEmpty = true;

    public ObservableCollection<DriverPackCatalogItem> DriverPacks { get; } = [];

    public ObservableCollection<DriverPackOptionItem> DriverPackOptions { get; } = [];

    public ObservableCollection<string> DriverPackModelOptions { get; } = [];

    public ObservableCollection<string> DriverPackVersionOptions { get; } = [];

    public int CatalogCount => DriverPacks.Count;

    public DriverPackSelectionKind EffectiveSelectionKind => GetEffectiveSelectionKind();

    public string DriverPackModeDisplay => SelectedDriverPackOption?.Kind switch
    {
        DriverPackSelectionKind.MicrosoftUpdateCatalog => "Microsoft Update Catalog",
        DriverPackSelectionKind.OemCatalog => "OEM Driver Pack",
        _ => "None"
    };

    public bool IsOemDriverSourceSelected => SelectedDriverPackOption?.Kind == DriverPackSelectionKind.OemCatalog;

    public bool IsDriverPackModelSelectionEnabled => IsOemDriverSourceSelected && DriverPackModelOptions.Count > 0;

    public bool IsDriverPackVersionSelectionEnabled => IsDriverPackModelSelectionEnabled && DriverPackVersionOptions.Count > 0;

    public string SelectedDriverPackSelectionDisplay => BuildSelectedDriverPackSelectionDisplay();

    public void ApplyCatalog(IReadOnlyList<DriverPackCatalogItem> driverPacks)
    {
        ArgumentNullException.ThrowIfNull(driverPacks);

        DriverPacks.Clear();
        foreach (DriverPackCatalogItem item in driverPacks)
        {
            DriverPacks.Add(item);
        }

        OnPropertyChanged(nameof(CatalogCount));
        RefreshDriverPackOptions();
    }

    public void UpdateSelectionContext(
        HardwareProfile? detectedHardware,
        OperatingSystemCatalogItem? selectedOperatingSystem,
        string effectiveArchitecture)
    {
        _detectedHardware = detectedHardware;
        _selectedOperatingSystem = selectedOperatingSystem;
        _effectiveArchitecture = NormalizeArchitecture(effectiveArchitecture);
        RefreshDriverPackOptions();
    }

    public void ReplaceCatalog(IReadOnlyList<DriverPackCatalogItem> driverPacks)
    {
        ApplyCatalog(driverPacks);
    }

    public void SetDetectedHardware(HardwareProfile? detectedHardware)
    {
        _detectedHardware = detectedHardware;
        RefreshDriverPackOptions();
    }

    public void SetOperatingSystemContext(OperatingSystemCatalogItem? selectedOperatingSystem, string effectiveArchitecture)
    {
        _selectedOperatingSystem = selectedOperatingSystem;
        _effectiveArchitecture = NormalizeArchitecture(effectiveArchitecture);
        RefreshDriverPackOptions();
    }

    public DriverPackSelectionKind GetEffectiveSelectionKind()
    {
        return SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
    }

    public bool HasValidSelection()
    {
        return GetEffectiveSelectionKind() != DriverPackSelectionKind.OemCatalog ||
               ResolveEffectiveDriverPackSelection() is not null;
    }

    partial void OnSelectedDriverPackOptionChanged(DriverPackOptionItem? value)
    {
        if (_isUpdatingDriverPackOptionSelection)
        {
            return;
        }

        _hasUserSelectedDriverPackOption = true;
        RefreshDriverPackModelAndVersionOptions();
    }

    partial void OnSelectedDriverPackModelChanged(string value)
    {
        RefreshDriverPackVersionOptions();
    }

    partial void OnSelectedDriverPackVersionChanged(string value)
    {
        NotifyDriverPackSelectionStateChanged();
    }

    partial void OnAutoSelectDriverPackWhenEmptyChanged(bool value)
    {
        RefreshDriverPackOptions();
    }

    public DriverPackCatalogItem? ResolveEffectiveDriverPackSelection()
    {
        DriverPackSelectionKind selectionKind = GetEffectiveSelectionKind();
        if (selectionKind != DriverPackSelectionKind.OemCatalog)
        {
            return SelectedDriverPackOption?.DriverPack;
        }

        DriverPackCatalogItem[] sourceCandidates = BuildSourceDriverPackCandidates();
        if (sourceCandidates.Length == 0)
        {
            return null;
        }

        DriverPackCatalogItem[] modelCandidates = FilterDriverPackCandidatesBySelectedModel(sourceCandidates);
        if (modelCandidates.Length == 0)
        {
            return null;
        }

        DriverPackCatalogItem[] versionCandidates = string.IsNullOrWhiteSpace(SelectedDriverPackVersion)
            ? modelCandidates
            : modelCandidates
                .Where(item => GetDriverPackVersionDisplay(item).Equals(SelectedDriverPackVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

        DriverPackCatalogItem[] finalCandidates = versionCandidates.Length > 0
            ? versionCandidates
            : modelCandidates;

        return finalCandidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public DriverPackCatalogItem? ResolveEffectiveSelection()
    {
        return ResolveEffectiveDriverPackSelection();
    }

    private void RefreshDriverPackOptions()
    {
        string previousKey = _hasUserSelectedDriverPackOption
            ? SelectedDriverPackOption?.Key ?? string.Empty
            : string.Empty;
        DriverPackOptionItem[] options = BuildDriverPackOptions();

        _isUpdatingDriverPackOptionSelection = true;
        try
        {
            DriverPackOptions.Clear();
            foreach (DriverPackOptionItem option in options)
            {
                DriverPackOptions.Add(option);
            }

            DriverPackOptionItem? selected = null;
            if (!string.IsNullOrWhiteSpace(previousKey))
            {
                selected = options.FirstOrDefault(option =>
                    option.Key.Equals(previousKey, StringComparison.OrdinalIgnoreCase));
            }

            selected ??= ResolveDefaultDriverPackOption(options);
            SelectedDriverPackOption = selected;
        }
        finally
        {
            _isUpdatingDriverPackOptionSelection = false;
        }

        RefreshDriverPackModelAndVersionOptions();
    }

    private DriverPackOptionItem[] BuildDriverPackOptions()
    {
        return
        [
            CreateNoneDriverPackOption(),
            CreateMicrosoftUpdateCatalogOption(),
            CreateOemDriverPackOption(DellDriverPackOptionKey, "Dell"),
            CreateOemDriverPackOption(LenovoDriverPackOptionKey, "Lenovo"),
            CreateOemDriverPackOption(HpDriverPackOptionKey, "HP"),
            CreateOemDriverPackOption(MicrosoftOemDriverPackOptionKey, "Microsoft")
        ];
    }

    private DriverPackCatalogItem[] BuildSourceDriverPackCandidates()
    {
        if (!IsOemDriverSourceSelected)
        {
            return [];
        }

        string sourceManufacturer = ResolveManufacturerFromSourceOptionKey(SelectedDriverPackOption?.Key ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceManufacturer))
        {
            return [];
        }

        return BuildFilteredDriverPackCandidates(forceManufacturer: sourceManufacturer);
    }

    private void RefreshDriverPackModelAndVersionOptions()
    {
        string previousModel = SelectedDriverPackModel;

        DriverPackModelOptions.Clear();
        DriverPackVersionOptions.Clear();
        SelectedDriverPackModel = string.Empty;
        SelectedDriverPackVersion = string.Empty;

        if (!IsOemDriverSourceSelected)
        {
            NotifyDriverPackSelectionStateChanged();
            return;
        }

        DriverPackCatalogItem[] sourceCandidates = BuildSourceDriverPackCandidates();
        string[] models = sourceCandidates
            .SelectMany(GetSelectableModelNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string model in models)
        {
            DriverPackModelOptions.Add(model);
        }

        if (models.Length > 0)
        {
            string preferredModel = models.FirstOrDefault(model =>
                model.Equals(previousModel, StringComparison.OrdinalIgnoreCase))
                ?? ResolvePreferredModelFromHardware(sourceCandidates, models);

            SelectedDriverPackModel = preferredModel;
        }

        NotifyDriverPackSelectionStateChanged();
    }

    private void RefreshDriverPackVersionOptions()
    {
        string previousVersion = SelectedDriverPackVersion;
        DriverPackVersionOptions.Clear();
        SelectedDriverPackVersion = string.Empty;

        if (!IsOemDriverSourceSelected || string.IsNullOrWhiteSpace(SelectedDriverPackModel))
        {
            NotifyDriverPackSelectionStateChanged();
            return;
        }

        DriverPackCatalogItem[] modelCandidates = FilterDriverPackCandidatesBySelectedModel(BuildSourceDriverPackCandidates());
        DriverPackCatalogItem[] orderedCandidates = SortDriverPackCandidates(modelCandidates);

        string[] versions = orderedCandidates
            .Select(GetDriverPackVersionDisplay)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string version in versions)
        {
            DriverPackVersionOptions.Add(version);
        }

        if (versions.Length > 0)
        {
            SelectedDriverPackVersion = versions.FirstOrDefault(version =>
                version.Equals(previousVersion, StringComparison.OrdinalIgnoreCase))
                ?? versions[0];
        }

        NotifyDriverPackSelectionStateChanged();
    }

    private DriverPackCatalogItem[] FilterDriverPackCandidatesBySelectedModel(IEnumerable<DriverPackCatalogItem> candidates)
    {
        if (string.IsNullOrWhiteSpace(SelectedDriverPackModel))
        {
            return candidates.ToArray();
        }

        string selectedModel = SelectedDriverPackModel.Trim();
        return candidates
            .Where(item => GetSelectableModelNames(item).Any(model =>
                model.Equals(selectedModel, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private string ResolvePreferredModelFromHardware(
        IReadOnlyList<DriverPackCatalogItem> sourceCandidates,
        IReadOnlyList<string> modelOptions)
    {
        if (modelOptions.Count == 0)
        {
            return string.Empty;
        }

        if (_detectedHardware is null)
        {
            return modelOptions[0];
        }

        string[] hardwareTokens =
        [
            _detectedHardware.Model.Trim(),
            _detectedHardware.Product.Trim()
        ];
        hardwareTokens = hardwareTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hardwareTokens.Length == 0)
        {
            return modelOptions[0];
        }

        string? exactOptionMatch = modelOptions.FirstOrDefault(option =>
            hardwareTokens.Any(token => option.Equals(token, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(exactOptionMatch))
        {
            return exactOptionMatch;
        }

        string? containsOptionMatch = modelOptions.FirstOrDefault(option =>
            hardwareTokens.Any(token => IsFuzzyModelMatch(option, token)));
        if (!string.IsNullOrWhiteSpace(containsOptionMatch))
        {
            return containsOptionMatch;
        }

        DriverPackCatalogItem? bestPackMatch = sourceCandidates
            .Where(item => item.ModelNames.Any(modelName =>
                hardwareTokens.Any(token => IsFuzzyModelMatch(modelName, token))))
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestPackMatch is not null)
        {
            string? modelFromPack = GetSelectableModelNames(bestPackMatch)
                .FirstOrDefault(model => modelOptions.Any(option =>
                    option.Equals(model, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(modelFromPack))
            {
                return modelFromPack;
            }
        }

        return modelOptions[0];
    }

    private DriverPackCatalogItem[] BuildFilteredDriverPackCandidates(string forceManufacturer = "")
    {
        IEnumerable<DriverPackCatalogItem> query = DriverPacks;

        string architecture = NormalizeArchitecture(_selectedOperatingSystem?.Architecture ?? _effectiveArchitecture);
        if (!string.IsNullOrWhiteSpace(architecture))
        {
            query = query.Where(item => IsArchitectureMatch(architecture, item.OsArchitecture));
        }

        string selectedOsRelease = _selectedOperatingSystem?.WindowsRelease?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedOsRelease))
        {
            string windowsLabel = $"Windows {selectedOsRelease}";
            query = query.Where(item =>
                item.OsName.Contains(windowsLabel, StringComparison.OrdinalIgnoreCase) ||
                item.OsName.Contains(selectedOsRelease, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(forceManufacturer))
        {
            query = query.Where(item =>
                NormalizeManufacturer(item.Manufacturer).Equals(forceManufacturer, StringComparison.OrdinalIgnoreCase));
        }

        DriverPackCatalogItem[] baseCandidates = query.ToArray();
        return SortDriverPackCandidates(baseCandidates);
    }

    private DriverPackOptionItem? ResolveDefaultDriverPackOption(IReadOnlyList<DriverPackOptionItem> options)
    {
        if (options.Count == 0)
        {
            return null;
        }

        if (!AutoSelectDriverPackWhenEmpty)
        {
            return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.None) ?? options[0];
        }

        if (_detectedHardware?.IsVirtualMachine == true)
        {
            return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
                   ?? options[0];
        }

        if (_detectedHardware is not null && _selectedOperatingSystem is not null && DriverPacks.Count > 0)
        {
            DriverPackSelectionResult selection = _driverPackSelectionService.SelectBest(
                DriverPacks.ToArray(),
                _detectedHardware,
                _selectedOperatingSystem);

            if (selection.DriverPack is not null)
            {
                string selectedKey = ResolveSourceOptionKey(selection.DriverPack.Manufacturer);
                DriverPackOptionItem? oemMatch = options.FirstOrDefault(option =>
                    option.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));

                if (oemMatch is not null)
                {
                    return oemMatch;
                }
            }
        }

        return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
               ?? options[0];
    }

    private static DriverPackOptionItem CreateNoneDriverPackOption()
    {
        return new DriverPackOptionItem
        {
            Key = NoneDriverPackOptionKey,
            DisplayName = "None",
            Kind = DriverPackSelectionKind.None,
            DriverPack = null
        };
    }

    private static DriverPackOptionItem CreateMicrosoftUpdateCatalogOption()
    {
        return new DriverPackOptionItem
        {
            Key = MicrosoftUpdateCatalogDriverPackOptionKey,
            DisplayName = "Microsoft Update Catalog",
            Kind = DriverPackSelectionKind.MicrosoftUpdateCatalog,
            DriverPack = null
        };
    }

    private static DriverPackOptionItem CreateOemDriverPackOption(string key, string displayName)
    {
        return new DriverPackOptionItem
        {
            Key = key,
            DisplayName = displayName,
            Kind = DriverPackSelectionKind.OemCatalog,
            DriverPack = null
        };
    }

    private string BuildSelectedDriverPackSelectionDisplay()
    {
        DriverPackSelectionKind selectionKind = GetEffectiveSelectionKind();
        if (selectionKind == DriverPackSelectionKind.None)
        {
            return "None";
        }

        if (selectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            return "Microsoft Update Catalog";
        }

        DriverPackCatalogItem? selectedPack = ResolveEffectiveDriverPackSelection();
        if (selectedPack is null)
        {
            string sourceName = SelectedDriverPackOption?.DisplayName ?? "OEM";
            return $"{sourceName} | No matching model/version";
        }

        string modelName = string.IsNullOrWhiteSpace(SelectedDriverPackModel)
            ? ResolveDriverPackFriendlyName(selectedPack)
            : SelectedDriverPackModel;
        string version = GetDriverPackVersionDisplay(selectedPack);

        return $"{selectedPack.Manufacturer} | {modelName} | {version}";
    }

    private void NotifyDriverPackSelectionStateChanged()
    {
        OnPropertyChanged(nameof(IsOemDriverSourceSelected));
        OnPropertyChanged(nameof(IsDriverPackModelSelectionEnabled));
        OnPropertyChanged(nameof(IsDriverPackVersionSelectionEnabled));
        OnPropertyChanged(nameof(SelectedDriverPackSelectionDisplay));
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ResolveManufacturerFromSourceOptionKey(string optionKey)
    {
        return optionKey.Trim().ToLowerInvariant() switch
        {
            DellDriverPackOptionKey => "dell",
            LenovoDriverPackOptionKey => "lenovo",
            HpDriverPackOptionKey => "hp",
            MicrosoftOemDriverPackOptionKey => "microsoft",
            _ => string.Empty
        };
    }

    private static string ResolveSourceOptionKey(string manufacturer)
    {
        string normalized = NormalizeManufacturer(manufacturer);
        return normalized switch
        {
            "dell" => DellDriverPackOptionKey,
            "lenovo" => LenovoDriverPackOptionKey,
            "hp" => HpDriverPackOptionKey,
            "microsoft" => MicrosoftOemDriverPackOptionKey,
            _ => string.Empty
        };
    }

    private static string[] GetSelectableModelNames(DriverPackCatalogItem driverPack)
    {
        string[] models = driverPack.ModelNames
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length > 0)
        {
            return models;
        }

        string fallback = ResolveDriverPackFriendlyName(driverPack);
        return string.IsNullOrWhiteSpace(fallback)
            ? []
            : [fallback];
    }

    private static string GetDriverPackVersionDisplay(DriverPackCatalogItem driverPack)
    {
        if (!string.IsNullOrWhiteSpace(driverPack.Version))
        {
            return driverPack.Version.Trim();
        }

        if (driverPack.ReleaseDate is not null)
        {
            return driverPack.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(driverPack.PackageId))
        {
            return driverPack.PackageId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(driverPack.FileName))
        {
            return Path.GetFileNameWithoutExtension(driverPack.FileName.Trim());
        }

        return "Unknown";
    }

    private static string ResolveDriverPackFriendlyName(DriverPackCatalogItem driverPack)
    {
        string[] models = driverPack.ModelNames
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length > 0)
        {
            return models.Length == 1
                ? models[0]
                : $"{models[0]} (+{models.Length - 1} models)";
        }

        if (!LooksLikeArchiveOrInstallerName(driverPack.Name))
        {
            return driverPack.Name.Trim();
        }

        if (!LooksLikeArchiveOrInstallerName(driverPack.PackageId))
        {
            return driverPack.PackageId.Trim();
        }

        string fallback = !string.IsNullOrWhiteSpace(driverPack.FileName)
            ? driverPack.FileName
            : driverPack.Name;

        return Path.GetFileNameWithoutExtension(fallback).Trim();
    }

    private static bool LooksLikeArchiveOrInstallerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string extension = Path.GetExtension(value.Trim());
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cab", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFuzzyModelMatch(string source, string value)
    {
        return ContainsIgnoreCase(source, value) || ContainsIgnoreCase(value, source);
    }

    private static DriverPackCatalogItem[] SortDriverPackCandidates(IEnumerable<DriverPackCatalogItem> candidates)
    {
        return candidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsArchitectureMatch(string osArchitecture, string driverArchitecture)
    {
        string os = NormalizeArchitecture(osArchitecture);
        string driver = NormalizeArchitecture(driverArchitecture);
        return os.Equals(driver, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchitecture(string architecture)
    {
        string normalized = architecture.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }

    private static string NormalizeManufacturer(string manufacturer)
    {
        string normalized = manufacturer.Trim().ToLowerInvariant();
        if (normalized.Contains("hewlett") || normalized == "hp")
        {
            return "hp";
        }

        if (normalized.Contains("dell"))
        {
            return "dell";
        }

        if (normalized.Contains("lenovo"))
        {
            return "lenovo";
        }

        if (normalized.Contains("microsoft"))
        {
            return "microsoft";
        }

        return normalized;
    }
}

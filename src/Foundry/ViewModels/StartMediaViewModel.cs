using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class StartMediaViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IAdkService adkService;
    private readonly IWinPeLanguageDiscoveryService languageDiscoveryService;
    private readonly IWinPeUsbMediaService usbMediaService;
    private readonly IExpertDeployConfigurationStateService expertDeployConfigurationStateService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
    private readonly ILogger logger;
    private IReadOnlyList<string> availableWinPeLanguages = [];

    public StartMediaViewModel(
        IAppSettingsService appSettingsService,
        IAdkService adkService,
        IWinPeLanguageDiscoveryService languageDiscoveryService,
        IWinPeUsbMediaService usbMediaService,
        IExpertDeployConfigurationStateService expertDeployConfigurationStateService,
        IApplicationLocalizationService localizationService,
        IAppDispatcher appDispatcher,
        ILogger logger)
    {
        this.appSettingsService = appSettingsService;
        this.adkService = adkService;
        this.languageDiscoveryService = languageDiscoveryService;
        this.usbMediaService = usbMediaService;
        this.expertDeployConfigurationStateService = expertDeployConfigurationStateService;
        this.localizationService = localizationService;
        this.appDispatcher = appDispatcher;
        this.logger = logger.ForContext<StartMediaViewModel>();

        Architectures =
        [
            new(WinPeArchitecture.X64, "x64"),
            new(WinPeArchitecture.Arm64, "arm64")
        ];
        PartitionStyles =
        [
            new(UsbPartitionStyle.Gpt, "GPT"),
            new(UsbPartitionStyle.Mbr, "MBR")
        ];
        FormatModes = [];

        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        SelectedPartitionStyle = SelectOption(PartitionStyles, ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt));
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;

        adkService.StatusChanged += OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged += OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        ApplyLocalizedText();
        RefreshEvaluation();
    }

    public ObservableCollection<SelectionOption<WinPeArchitecture>> Architectures { get; }
    public ObservableCollection<SelectionOption<UsbPartitionStyle>> PartitionStyles { get; }
    public ObservableCollection<SelectionOption<UsbFormatMode>> FormatModes { get; }
    public ObservableCollection<SelectionOption<WinPeUsbDiskCandidate>> UsbCandidates { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IsoOutputPath { get; set; }

    [ObservableProperty]
    public partial SelectionOption<WinPeArchitecture>? SelectedArchitecture { get; set; }

    [ObservableProperty]
    public partial bool UseCa2023Signature { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbPartitionStyle>? SelectedPartitionStyle { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbFormatMode>? SelectedFormatMode { get; set; }

    [ObservableProperty]
    public partial bool IncludeDellDrivers { get; set; }

    [ObservableProperty]
    public partial bool IncludeHpDrivers { get; set; }

    [ObservableProperty]
    public partial string CustomDriverDirectoryPath { get; set; }

    [ObservableProperty]
    public partial SelectionOption<WinPeUsbDiskCandidate>? SelectedUsbDisk { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingUsbCandidates { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateIsoSummary { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateUsbSummary { get; set; }

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FinalExecutionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GlobalSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WinPeLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UsbCandidateStatus { get; set; } = string.Empty;

    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged -= OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    public async Task InitializeAsync()
    {
        RefreshEvaluation();

        if (adkService.CurrentStatus.CanCreateMedia)
        {
            await RefreshUsbCandidatesAsync();
        }

        MediaPreflightOptions options = CreatePreflightOptions();
        LogPreflightSummary(options, MediaPreflightService.Evaluate(options));
    }

    [RelayCommand]
    private async Task RefreshUsbCandidatesAsync()
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            UsbCandidateStatus = localizationService.GetString("StartMedia.Usb.AdkBlocked");
            RefreshEvaluation();
            return;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            UsbCandidateStatus = toolsResult.Error?.Message ?? localizationService.GetString("StartMedia.Usb.QueryFailed");
            logger.Warning("USB target refresh skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            RefreshEvaluation();
            return;
        }

        IsRefreshingUsbCandidates = true;

        try
        {
            WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await usbMediaService.GetUsbCandidatesAsync(
                toolsResult.Value,
                Constants.UsbQueryTempDirectoryPath,
                CancellationToken.None);

                UsbCandidates.Clear();
                if (result.IsSuccess && result.Value is not null)
                {
                    foreach (WinPeUsbDiskCandidate candidate in result.Value)
                    {
                    UsbCandidates.Add(CreateUsbDiskOption(candidate));
                    }

                SelectedUsbDisk = UsbCandidates.FirstOrDefault(option => option.Value.DiskNumber == SelectedUsbDisk?.Value.DiskNumber)
                    ?? UsbCandidates.FirstOrDefault();
                UsbCandidateStatus = string.Format(localizationService.GetString("StartMedia.Usb.CandidatesFound"), UsbCandidates.Count);
                logger.Information("USB targets refreshed. CandidateCount={CandidateCount}", UsbCandidates.Count);
            }
            else
            {
                SelectedUsbDisk = null;
                UsbCandidateStatus = result.Error?.Message ?? localizationService.GetString("StartMedia.Usb.QueryFailed");
                logger.Warning("USB target refresh failed. ErrorCode={ErrorCode}", result.Error?.Code);
            }
        }
        finally
        {
            IsRefreshingUsbCandidates = false;
            RefreshEvaluation();
        }
    }

    partial void OnSelectedUsbDiskChanged(SelectionOption<WinPeUsbDiskCandidate>? value)
    {
        RefreshEvaluation();
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(RefreshEvaluation))
        {
            logger.Warning("Failed to enqueue Start page ADK status refresh. IsReady={IsReady}", e.Status.CanCreateMedia);
        }
    }

    private void OnExpertDeployConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (!appDispatcher.TryEnqueue(RefreshEvaluation))
        {
            logger.Warning("Failed to enqueue Start page Expert Deploy configuration refresh.");
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() =>
        {
            ApplyLocalizedText();
            RefreshEvaluation();
        }))
        {
            logger.Warning(
                "Failed to enqueue Start page localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyLocalizedText()
    {
        PageTitle = localizationService.GetString("StartPage_Title.Text");
        FinalExecutionStatus = localizationService.GetString("StartMedia.FinalExecution.Deferred");
        RebuildFormatModes();
        RebuildUsbCandidateDisplayNames();
    }

    private void RefreshEvaluation()
    {
        LoadConfigurationFromSettings();
        WinPeLanguage = NormalizeCultureName(appSettingsService.Current.Media.WinPeLanguage);
        availableWinPeLanguages = GetAvailableWinPeLanguages();
        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        CanGenerateIsoSummary = evaluation.CanGenerateIsoSummary;
        CanGenerateUsbSummary = evaluation.CanGenerateUsbSummary;
        StatusSummary = BuildStatusText(evaluation);
        GlobalSummary = BuildGlobalSummary(options, evaluation);

    }

    private void LoadConfigurationFromSettings()
    {
        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        SelectedPartitionStyle = SelectOption(PartitionStyles, ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt));
        SelectedFormatMode = SelectOption(FormatModes, ParseEnum(appSettingsService.Current.Media.UsbFormatMode, UsbFormatMode.Quick));
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;
    }

    private MediaPreflightOptions CreatePreflightOptions()
    {
        List<WinPeVendorSelection> vendors = [];
        if (IncludeDellDrivers)
        {
            vendors.Add(WinPeVendorSelection.Dell);
        }

        if (IncludeHpDrivers)
        {
            vendors.Add(WinPeVendorSelection.Hp);
        }

        return new MediaPreflightOptions
        {
            IsAdkReady = adkService.CurrentStatus.CanCreateMedia,
            IsRuntimePayloadReady = false,
            IsNetworkConfigurationReady = expertDeployConfigurationStateService.IsNetworkConfigurationReady,
            IsDeployConfigurationReady = expertDeployConfigurationStateService.IsDeployConfigurationReady,
            IsConnectProvisioningReady = expertDeployConfigurationStateService.IsConnectProvisioningReady,
            AreRequiredSecretsReady = expertDeployConfigurationStateService.AreRequiredSecretsReady,
            IsFinalExecutionEnabled = false,
            IsoOutputPath = IsoOutputPath,
            Architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64,
            SignatureMode = UseCa2023Signature ? WinPeSignatureMode.Pca2023 : WinPeSignatureMode.Pca2011,
            UsbPartitionStyle = SelectedPartitionStyle?.Value ?? UsbPartitionStyle.Gpt,
            UsbFormatMode = SelectedFormatMode?.Value ?? UsbFormatMode.Quick,
            WinPeLanguage = WinPeLanguage,
            AvailableWinPeLanguages = availableWinPeLanguages,
            BootImageSource = WinPeBootImageSource.WinPe,
            DriverVendors = vendors,
            CustomDriverDirectoryPath = CustomDriverDirectoryPath,
            SelectedUsbDisk = SelectedUsbDisk?.Value
        };
    }

    private string BuildStatusText(MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);

        return string.Format(
            localizationService.GetString("StartMedia.Status"),
            FormatReady(evaluation.CanGenerateIsoSummary || evaluation.CanGenerateUsbSummary),
            reasons.Count);
    }

    private string BuildGlobalSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);
        var builder = new StringBuilder();
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Adk")}: {FormatReady(adkService.CurrentStatus.CanCreateMedia)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.WinPeLanguage")}: {FormatValue(NormalizeCultureName(options.WinPeLanguage))}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Architecture")}: {FormatArchitecture(options.Architecture)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.IsoPath")}: {FormatValue(options.IsoOutputPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.UsbTarget")}: {FormatUsbCandidate(options.SelectedUsbDisk)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Signature")}: {FormatSignatureMode(options.SignatureMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.PartitionStyle")}: {evaluation.EffectiveUsbPartitionStyle}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.FormatMode")}: {FormatUsbFormatMode(options.UsbFormatMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Drivers")}: {FormatDriverOptions(options.DriverVendors, options.CustomDriverDirectoryPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Runtime")}: {FormatReady(options.IsRuntimePayloadReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Network")}: {FormatReady(options.IsNetworkConfigurationReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Deploy")}: {FormatReady(options.IsDeployConfigurationReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Connect")}: {FormatReady(options.IsConnectProvisioningReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Secrets")}: {FormatReady(options.AreRequiredSecretsReady)}");
        builder.AppendLine();
        builder.AppendLine(localizationService.GetString("StartMedia.FinalExecution.Deferred"));

        AppendBlockingReasons(builder, reasons);

        return builder.ToString();
    }

    private void AppendBlockingReasons(StringBuilder builder, IReadOnlyList<MediaPreflightBlockingReason> reasons)
    {
        if (reasons.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(localizationService.GetString("StartMedia.Summary.BlockingReasons"));
        foreach (MediaPreflightBlockingReason reason in reasons)
        {
            builder.AppendLine($"- {GetBlockingReasonText(reason)}");
        }
    }

    private string GetBlockingReasonText(MediaPreflightBlockingReason reason)
    {
        return localizationService.GetString($"StartMedia.BlockingReason.{reason}");
    }

    private void LogPreflightSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);

        logger.Information(
            "Media preflight summary refreshed. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, BootImageSource={BootImageSource}, IsoOutputPath={IsoOutputPath}, UsbTargetSelected={UsbTargetSelected}, DiskNumber={DiskNumber}, DiskName={DiskName}, RuntimeReady={RuntimeReady}, NetworkReady={NetworkReady}, DeployReady={DeployReady}, ConnectReady={ConnectReady}, SecretsReady={SecretsReady}, SummaryReady={SummaryReady}, BlockingReasons={BlockingReasons}",
            options.Architecture,
            NormalizeCultureName(options.WinPeLanguage),
            options.BootImageSource,
            options.IsoOutputPath,
            options.SelectedUsbDisk is not null,
            options.SelectedUsbDisk?.DiskNumber,
            options.SelectedUsbDisk?.FriendlyName,
            options.IsRuntimePayloadReady,
            options.IsNetworkConfigurationReady,
            options.IsDeployConfigurationReady,
            options.IsConnectProvisioningReady,
            options.AreRequiredSecretsReady,
            evaluation.CanGenerateIsoSummary || evaluation.CanGenerateUsbSummary,
            string.Join(",", reasons));
    }

    private static IReadOnlyList<MediaPreflightBlockingReason> GetGlobalBlockingReasons(MediaPreflightEvaluation evaluation)
    {
        return evaluation.IsoBlockingReasons
            .Concat(evaluation.UsbBlockingReasons)
            .Where(reason => reason != MediaPreflightBlockingReason.NoUsbTarget)
            .Distinct()
            .ToList();
    }

    private IReadOnlyList<string> GetAvailableWinPeLanguages()
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            return [];
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            logger.Warning("WinPE language validation skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            return [];
        }

        WinPeResult<IReadOnlyList<string>> languagesResult = languageDiscoveryService.GetAvailableLanguages(
            new WinPeLanguageDiscoveryOptions
            {
                Tools = toolsResult.Value,
                Architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64
            });

        if (!languagesResult.IsSuccess || languagesResult.Value is null)
        {
            logger.Warning("WinPE language validation skipped because language discovery failed. ErrorCode={ErrorCode}", languagesResult.Error?.Code);
            return [];
        }

        return languagesResult.Value.Select(NormalizeCultureName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void RebuildFormatModes()
    {
        UsbFormatMode selectedValue = SelectedFormatMode?.Value
            ?? ParseEnum(appSettingsService.Current.Media.UsbFormatMode, UsbFormatMode.Quick);

        FormatModes.Clear();
        FormatModes.Add(new(UsbFormatMode.Quick, localizationService.GetString("StartMedia.FormatMode.Quick")));
        FormatModes.Add(new(UsbFormatMode.Complete, localizationService.GetString("StartMedia.FormatMode.Complete")));
        SelectedFormatMode = SelectOption(FormatModes, selectedValue);
    }

    private void RebuildUsbCandidateDisplayNames()
    {
        List<WinPeUsbDiskCandidate> candidates = UsbCandidates.Select(option => option.Value).ToList();
        WinPeUsbDiskCandidate? selectedCandidate = SelectedUsbDisk?.Value;

        UsbCandidates.Clear();
        foreach (WinPeUsbDiskCandidate candidate in candidates)
        {
            UsbCandidates.Add(CreateUsbDiskOption(candidate));
        }

        SelectedUsbDisk = UsbCandidates.FirstOrDefault(option => option.Value.DiskNumber == selectedCandidate?.DiskNumber)
            ?? UsbCandidates.FirstOrDefault();
    }

    private SelectionOption<WinPeUsbDiskCandidate> CreateUsbDiskOption(WinPeUsbDiskCandidate candidate)
    {
        return new SelectionOption<WinPeUsbDiskCandidate>(
            candidate,
            string.Format(
                localizationService.GetString("StartMedia.Usb.DiskDisplay"),
                candidate.DiskNumber,
                candidate.FriendlyName,
                FormatByteSize(candidate.SizeBytes)));
    }

    private string FormatReady(bool isReady)
    {
        return isReady ? localizationService.GetString("Common.Enabled") : localizationService.GetString("Common.Disabled");
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string NormalizeCultureName(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language).Name;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    private string FormatDriverOptions(IReadOnlyList<WinPeVendorSelection> vendors, string? customDriverDirectoryPath)
    {
        List<string> parts = vendors.Select(FormatDriverVendor).ToList();
        if (!string.IsNullOrWhiteSpace(customDriverDirectoryPath))
        {
            parts.Add(customDriverDirectoryPath);
        }

        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private string FormatUsbCandidate(WinPeUsbDiskCandidate? candidate)
    {
        if (candidate is null)
        {
            return "-";
        }

        return string.Format(
            localizationService.GetString("StartMedia.Usb.DiskDisplay"),
            candidate.DiskNumber,
            candidate.FriendlyName,
            FormatByteSize(candidate.SizeBytes));
    }

    private string FormatArchitecture(WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "x64",
            WinPeArchitecture.Arm64 => "arm64",
            _ => architecture.ToString()
        };
    }

    private string FormatSignatureMode(WinPeSignatureMode signatureMode)
    {
        return localizationService.GetString($"StartMedia.SignatureMode.{signatureMode}");
    }

    private string FormatUsbFormatMode(UsbFormatMode formatMode)
    {
        return localizationService.GetString($"StartMedia.FormatMode.{formatMode}");
    }

    private string FormatDriverVendor(WinPeVendorSelection vendor)
    {
        return localizationService.GetString($"StartMedia.DriverVendor.{vendor}");
    }

    private string FormatByteSize(ulong bytes)
    {
        double size = bytes;
        string[] units =
        [
            localizationService.GetString("StartMedia.ByteUnit.B"),
            localizationService.GetString("StartMedia.ByteUnit.KB"),
            localizationService.GetString("StartMedia.ByteUnit.MB"),
            localizationService.GetString("StartMedia.ByteUnit.GB"),
            localizationService.GetString("StartMedia.ByteUnit.TB")
        ];
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out T result) ? result : fallback;
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }
}

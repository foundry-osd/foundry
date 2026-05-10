using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentSessionViewModel : LocalizedViewModelBase
{
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IProcessRunner _processRunner;
    private readonly bool _isDebugSafeMode;
    private string _rawDeploymentStatus = "Ready";
    private string _rawCurrentStepName = "Waiting for deployment...";
    private string _rawCurrentStepProgressText = "Waiting for progress...";
    private string _rawFailedStepName = string.Empty;
    private string _rawFailedStepErrorMessage = string.Empty;
    private DispatcherTimer? _elapsedTimeTimer;
    private DispatcherTimer? _rebootCountdownTimer;
    private DateTimeOffset? _deploymentStartTimeUtc;
    private int _activeStepIndex;
    private int _plannedStepCount;
    private string _lastLogsDirectoryPath = string.Empty;
    private bool _isDeploymentInProgress;
    private bool _isRebootInProgress;
    private bool _isDisposed;

    public DeploymentSessionViewModel(
        Dispatcher dispatcher,
        ILogger logger,
        IOperationProgressService operationProgressService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IProcessRunner processRunner,
        ILocalizationService localizationService,
        bool isDebugSafeMode)
        : base(localizationService)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationProgressService = operationProgressService ?? throw new ArgumentNullException(nameof(operationProgressService));
        _deploymentOrchestrator = deploymentOrchestrator ?? throw new ArgumentNullException(nameof(deploymentOrchestrator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _isDebugSafeMode = isDebugSafeMode;

        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged += OnStepProgressChanged;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartupReady))]
    private bool isStartupInitializing = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplashPage))]
    [NotifyPropertyChangedFor(nameof(IsSuccessPage))]
    private DeploymentPage currentPage = DeploymentPage.Splash;

    [ObservableProperty]
    private string deploymentStatus = LocalizationText.GetString("Status.Ready");

    [ObservableProperty]
    private int deploymentProgress;

    [ObservableProperty]
    private bool isGlobalProgressIndeterminate = true;

    [ObservableProperty]
    private string globalProgressPercentText = "0%";

    [ObservableProperty]
    private string currentStepName = LocalizationText.GetString("Status.WaitingForDeployment");

    [ObservableProperty]
    private string stepCounterText = LocalizationText.GetString("Status.StepCounterUnknown");

    [ObservableProperty]
    private double currentStepProgress;

    [ObservableProperty]
    private bool isCurrentStepProgressIndeterminate = true;

    [ObservableProperty]
    private string currentStepProgressText = LocalizationText.GetString("Status.WaitingForProgress");

    [ObservableProperty]
    private string computerNameText = string.Empty;

    [ObservableProperty]
    private string ipAddress = LocalizationText.GetString("Common.NotAvailable");

    [ObservableProperty]
    private string subnetMask = LocalizationText.GetString("Common.NotAvailable");

    [ObservableProperty]
    private string gatewayAddress = LocalizationText.GetString("Common.NotAvailable");

    [ObservableProperty]
    private string macAddress = LocalizationText.GetString("Common.NotAvailable");

    [ObservableProperty]
    private string startTimeText = LocalizationText.GetString("Common.NotAvailable");

    [ObservableProperty]
    private string elapsedTimeText = "00:00:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebootNowCommand))]
    [NotifyPropertyChangedFor(nameof(RebootCountdownText))]
    private int rebootCountdownSeconds = 10;

    [ObservableProperty]
    private string failedStepName = string.Empty;

    [ObservableProperty]
    private string failedStepErrorMessage = string.Empty;

    public bool IsSplashPage => CurrentPage == DeploymentPage.Splash;

    public bool IsSuccessPage => CurrentPage == DeploymentPage.Success;

    public bool IsStartupReady => !IsStartupInitializing;

    public int PlannedStepCount => _deploymentOrchestrator.PlannedSteps.Count;
    public string RebootCountdownText => Format("Success.RebootCountdownFormat", RebootCountdownSeconds);

    public void SetStatus(string status)
    {
        _rawDeploymentStatus = status;
        DeploymentStatus = DeploymentUiTextLocalizer.LocalizeMessage(status);
    }

    public void SetComputerName(string computerName)
    {
        ComputerNameText = computerName;
    }

    public void CompleteStartupInitialization()
    {
        IsStartupInitializing = false;
        CurrentPage = DeploymentPage.Splash;
    }

    public void ShowWizard()
    {
        if (IsStartupInitializing)
        {
            return;
        }

        CurrentPage = DeploymentPage.Wizard;
    }

    public void BeginDeployment(string computerName, int plannedStepCount)
    {
        _isDeploymentInProgress = true;
        _lastLogsDirectoryPath = string.Empty;
        ClearFailureDetails();
        _plannedStepCount = plannedStepCount;
        _activeStepIndex = 0;

        DeploymentProgress = 0;
        UpdateGlobalProgressVisuals(0);
        SetCurrentStepName("Preparing deployment...");
        CurrentStepProgress = 0;
        IsCurrentStepProgressIndeterminate = true;
        SetCurrentStepProgressText("Waiting for progress...");
        StepCounterText = BuildStepCounterText(0);
        ComputerNameText = computerName;
        CaptureNetworkSnapshot();

        _deploymentStartTimeUtc = DateTimeOffset.Now;
        StartTimeText = _deploymentStartTimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ElapsedTimeText = "00:00:00";
        StartElapsedTimeTracking();

        SetStatus("Deployment started.");
        CurrentPage = DeploymentPage.Progress;
    }

    public void CompleteDeployment(string status, string? logsDirectoryPath)
    {
        _isDeploymentInProgress = false;
        _lastLogsDirectoryPath = logsDirectoryPath ?? string.Empty;
        SetStatus(status);
        CurrentPage = DeploymentPage.Success;
    }

    public void FailDeployment(string status, string? stepName, string? errorMessage, string? logsDirectoryPath = null)
    {
        _isDeploymentInProgress = false;
        _lastLogsDirectoryPath = logsDirectoryPath ?? _lastLogsDirectoryPath;
        SetFailureDetails(stepName, errorMessage);
        SetStatus(status);
        CurrentPage = DeploymentPage.Error;
    }

    public void ApplyExecutionRunResult(DeploymentExecutionRunResult executionRunResult)
    {
        ArgumentNullException.ThrowIfNull(executionRunResult);

        if (executionRunResult.IsSuccess)
        {
            CompleteDeployment("Deployment completed.", executionRunResult.LogsDirectoryPath);
            return;
        }

        string fallbackStep = string.IsNullOrWhiteSpace(FailedStepName)
            ? CurrentStepName
            : FailedStepName;
        string fallbackMessage = string.IsNullOrWhiteSpace(FailedStepErrorMessage)
            ? executionRunResult.Message
            : FailedStepErrorMessage;

        FailDeployment(
            $"Deployment failed: {executionRunResult.Message}",
            fallbackStep,
            fallbackMessage,
            executionRunResult.LogsDirectoryPath);
    }

    public void ShowDebugProgress(string computerName, int currentStepIndex, int plannedStepCount, string currentStepName, int progressPercent)
    {
        _isDeploymentInProgress = false;
        ClearFailureDetails();
        _plannedStepCount = plannedStepCount;
        _activeStepIndex = currentStepIndex;
        DeploymentProgress = progressPercent;
        UpdateGlobalProgressVisuals(progressPercent);
        ComputerNameText = computerName;
        SetCurrentStepName(currentStepName);
        StepCounterText = BuildStepCounterText(currentStepIndex);
        CurrentStepProgress = 65;
        IsCurrentStepProgressIndeterminate = false;
        SetCurrentStepProgressText("Applying image: 65%");
        CaptureNetworkSnapshot();
        _deploymentStartTimeUtc = DateTimeOffset.Now;
        StartTimeText = _deploymentStartTimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ElapsedTimeText = "00:00:00";
        StartElapsedTimeTracking();
        SetStatus("Debug preview: progress page.");
        CurrentPage = DeploymentPage.Progress;
    }

    public void ShowDebugSuccess(string computerName, int plannedStepCount, string finalStepName)
    {
        _isDeploymentInProgress = false;
        StopElapsedTimeTracking();
        ClearFailureDetails();
        _plannedStepCount = plannedStepCount;
        DeploymentProgress = 100;
        UpdateGlobalProgressVisuals(100);
        ComputerNameText = computerName;
        SetCurrentStepName(finalStepName);
        StepCounterText = BuildStepCounterText(plannedStepCount);
        CurrentStepProgress = 100;
        IsCurrentStepProgressIndeterminate = false;
        SetCurrentStepProgressText("Step completed.");
        SetStatus("Debug preview: success page.");
        CurrentPage = DeploymentPage.Success;
    }

    public void ShowDebugError(string computerName, int currentStepIndex, string failedStepName, string failedStepErrorMessage)
    {
        _isDeploymentInProgress = false;
        StopElapsedTimeTracking();
        ComputerNameText = computerName;
        StepCounterText = BuildStepCounterText(currentStepIndex);
        SetFailureDetails(failedStepName, failedStepErrorMessage);
        SetStatus("Debug preview: error page.");
        CurrentPage = DeploymentPage.Error;
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            string logFilePath = ResolveEffectiveLogFilePath();
            ProcessStartInfo startInfo = new("notepad.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(logFilePath);
            _ = Process.Start(startInfo);
            _logger.LogInformation("Opened log file at {LogFilePath}.", logFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log file.");
            SetStatus($"Unable to open log file: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRebootNow))]
    private async Task RebootNowAsync()
    {
        await ExecuteRebootAsync().ConfigureAwait(false);
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            if (!_isDeploymentInProgress &&
                !_operationProgressService.IsOperationInProgress &&
                string.IsNullOrWhiteSpace(_operationProgressService.Status))
            {
                return;
            }

            int normalizedProgress = Math.Clamp(_operationProgressService.Progress, 0, 100);
            DeploymentProgress = Math.Max(DeploymentProgress, normalizedProgress);
            UpdateGlobalProgressVisuals(DeploymentProgress);

            if (!string.IsNullOrWhiteSpace(_operationProgressService.Status))
            {
                SetStatus(_operationProgressService.Status!);
            }
        });
    }

    private void OnStepProgressChanged(object? sender, DeploymentStepProgress stepProgress)
    {
        RunOnUi(() =>
        {
            if (stepProgress.StepIndex != _activeStepIndex)
            {
                _activeStepIndex = stepProgress.StepIndex;
                CurrentStepProgress = 0;
                IsCurrentStepProgressIndeterminate = true;
                SetCurrentStepProgressText("Starting step...");
            }

            SetCurrentStepName(stepProgress.StepName);
            StepCounterText = BuildStepCounterText(stepProgress.StepIndex);

            DeploymentProgress = Math.Max(DeploymentProgress, stepProgress.ProgressPercent);
            UpdateGlobalProgressVisuals(DeploymentProgress);
            UpdateCurrentStepProgressVisuals(stepProgress);

            if (!string.IsNullOrWhiteSpace(stepProgress.Message))
            {
                SetStatus(stepProgress.Message);
            }

            if (stepProgress.State == DeploymentStepState.Failed)
            {
                SetFailureDetails(stepProgress.StepName, stepProgress.Message ?? "Step failed.");
            }
        });
    }

    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged -= OnStepProgressChanged;
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        StopElapsedTimeTracking();
        StopRebootCountdown(resetSeconds: false);
        _isDisposed = true;
        base.Dispose();
    }

    partial void OnCurrentPageChanged(DeploymentPage value)
    {
        if (value == DeploymentPage.Success)
        {
            StartRebootCountdown();
        }
        else
        {
            StopRebootCountdown(resetSeconds: true);
        }

        if (value != DeploymentPage.Progress && !_isDeploymentInProgress)
        {
            StopElapsedTimeTracking();
        }

        RebootNowCommand.NotifyCanExecuteChanged();
    }

    private void UpdateGlobalProgressVisuals(int progressValue)
    {
        int clampedProgress = Math.Clamp(progressValue, 0, 100);
        GlobalProgressPercentText = $"{clampedProgress}%";
        IsGlobalProgressIndeterminate = _isDeploymentInProgress && clampedProgress <= 0;
    }

    private void UpdateCurrentStepProgressVisuals(DeploymentStepProgress stepProgress)
    {
        if (stepProgress.State == DeploymentStepState.Succeeded)
        {
            CurrentStepProgress = 100;
            IsCurrentStepProgressIndeterminate = false;
            SetCurrentStepProgressText(stepProgress.StepSubProgressLabel ?? "Step completed.");
            return;
        }

        if (stepProgress.State == DeploymentStepState.Failed)
        {
            IsCurrentStepProgressIndeterminate = false;
            SetCurrentStepProgressText(stepProgress.Message ?? "Step failed.");
            return;
        }

        if (stepProgress.State == DeploymentStepState.Skipped)
        {
            IsCurrentStepProgressIndeterminate = false;
            SetCurrentStepProgressText(stepProgress.Message ?? "Step skipped.");
            return;
        }

        if (stepProgress.StepSubProgressPercent.HasValue)
        {
            double normalized = Math.Clamp(stepProgress.StepSubProgressPercent.Value, 0d, 100d);
            CurrentStepProgress = normalized;
            IsCurrentStepProgressIndeterminate = false;
            SetCurrentStepProgressText(string.IsNullOrWhiteSpace(stepProgress.StepSubProgressLabel)
                ? $"{normalized:0.#}%"
                : stepProgress.StepSubProgressLabel!);
            return;
        }

        if (stepProgress.StepSubProgressIndeterminate)
        {
            IsCurrentStepProgressIndeterminate = true;
            SetCurrentStepProgressText(stepProgress.StepSubProgressLabel ?? "In progress...");
        }
    }

    private void StartElapsedTimeTracking()
    {
        StopElapsedTimeTracking();
        _elapsedTimeTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimeTimer.Tick += OnElapsedTimeTick;
        _elapsedTimeTimer.Start();
    }

    private void StopElapsedTimeTracking()
    {
        if (_elapsedTimeTimer is null)
        {
            return;
        }

        _elapsedTimeTimer.Tick -= OnElapsedTimeTick;
        _elapsedTimeTimer.Stop();
        _elapsedTimeTimer = null;
    }

    private void OnElapsedTimeTick(object? sender, EventArgs e)
    {
        if (!_deploymentStartTimeUtc.HasValue)
        {
            return;
        }

        TimeSpan elapsed = DateTimeOffset.Now - _deploymentStartTimeUtc.Value;
        ElapsedTimeText = elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void StartRebootCountdown()
    {
        StopRebootCountdown(resetSeconds: false);
        RebootCountdownSeconds = 10;
        _rebootCountdownTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _rebootCountdownTimer.Tick += OnRebootCountdownTick;
        _rebootCountdownTimer.Start();
    }

    private void StopRebootCountdown(bool resetSeconds)
    {
        if (_rebootCountdownTimer is not null)
        {
            _rebootCountdownTimer.Tick -= OnRebootCountdownTick;
            _rebootCountdownTimer.Stop();
            _rebootCountdownTimer = null;
        }

        if (resetSeconds)
        {
            RebootCountdownSeconds = 10;
        }
    }

    private void OnRebootCountdownTick(object? sender, EventArgs e)
    {
        if (RebootCountdownSeconds > 0)
        {
            RebootCountdownSeconds--;
        }

        if (RebootCountdownSeconds > 0)
        {
            return;
        }

        StopRebootCountdown(resetSeconds: false);
        if (!_isDebugSafeMode)
        {
            _ = ExecuteRebootAsync();
        }
    }

    private bool CanRebootNow()
    {
        return IsSuccessPage && !_isDebugSafeMode && !_isRebootInProgress;
    }

    private async Task ExecuteRebootAsync()
    {
        if (_isDebugSafeMode || _isRebootInProgress)
        {
            return;
        }

        _isRebootInProgress = true;
        RebootNowCommand.NotifyCanExecuteChanged();
        StopRebootCountdown(resetSeconds: false);

        try
        {
            string rebootExecutablePath = Path.Combine(Environment.SystemDirectory, "wpeutil.exe");
            if (!File.Exists(rebootExecutablePath))
            {
                throw new FileNotFoundException("Required reboot executable 'wpeutil.exe' was not found.", rebootExecutablePath);
            }

            SetStatus("Rebooting now...");

            ProcessExecutionResult result = await _processRunner
                .RunAsync(rebootExecutablePath, "Reboot", Path.GetTempPath())
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return;
            }

            RunOnUi(() =>
            {
                string diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                SetFailureDetails("System reboot", $"wpeutil.exe failed with exit code {result.ExitCode}. {diagnostic}".Trim());
                SetStatus("Reboot command failed.");
                CurrentPage = DeploymentPage.Error;
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                SetFailureDetails("System reboot", ex.Message);
                SetStatus($"Reboot command failed: {ex.Message}");
                CurrentPage = DeploymentPage.Error;
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                _isRebootInProgress = false;
                RebootNowCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private void SetFailureDetails(string? stepName, string? errorMessage)
    {
        _rawFailedStepName = string.IsNullOrWhiteSpace(stepName) ? "Unknown step" : stepName;
        _rawFailedStepErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "No error details were provided." : errorMessage;
        FailedStepName = DeploymentUiTextLocalizer.LocalizeStepName(_rawFailedStepName);
        FailedStepErrorMessage = DeploymentUiTextLocalizer.LocalizeMessage(_rawFailedStepErrorMessage);
    }

    private void ClearFailureDetails()
    {
        _rawFailedStepName = string.Empty;
        _rawFailedStepErrorMessage = string.Empty;
        FailedStepName = string.Empty;
        FailedStepErrorMessage = string.Empty;
    }

    private void CaptureNetworkSnapshot()
    {
        string notAvailable = GetString("Common.NotAvailable");
        IpAddress = notAvailable;
        SubnetMask = notAvailable;
        GatewayAddress = notAvailable;
        MacAddress = notAvailable;

        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();
                UnicastIPAddressInformation? ipv4AddressInfo = ipProperties.UnicastAddresses
                    .FirstOrDefault(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4AddressInfo is null)
                {
                    continue;
                }

                GatewayIPAddressInformation? gatewayInfo = ipProperties.GatewayAddresses
                    .FirstOrDefault(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                byte[] macBytes = networkInterface.GetPhysicalAddress().GetAddressBytes();

                IpAddress = ipv4AddressInfo.Address.ToString();
                SubnetMask = ipv4AddressInfo.IPv4Mask?.ToString() ?? notAvailable;
                GatewayAddress = gatewayInfo?.Address.ToString() ?? notAvailable;
                MacAddress = macBytes.Length == 0
                    ? notAvailable
                    : string.Join("-", macBytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to resolve network snapshot for deployment session.");
        }
    }

    private string BuildStepCounterText(int currentStep)
    {
        if (_plannedStepCount <= 0)
        {
            return GetString("Status.StepCounterUnknown");
        }

        int normalizedStep = Math.Clamp(currentStep, 0, _plannedStepCount);
        return Format("Status.StepCounterFormat", normalizedStep, _plannedStepCount);
    }

    private string ResolveEffectiveLogFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_lastLogsDirectoryPath))
        {
            return Path.Combine(_lastLogsDirectoryPath, FoundryDeployLogging.LogFileName);
        }

        return FoundryDeployLogging.ResolveStartupLogFilePath();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private void SetCurrentStepName(string value)
    {
        _rawCurrentStepName = value;
        CurrentStepName = DeploymentUiTextLocalizer.LocalizeStepName(value);
    }

    private void SetCurrentStepProgressText(string value)
    {
        _rawCurrentStepProgressText = value;
        CurrentStepProgressText = DeploymentUiTextLocalizer.LocalizeMessage(value);
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            DeploymentStatus = DeploymentUiTextLocalizer.LocalizeMessage(_rawDeploymentStatus);
            CurrentStepName = DeploymentUiTextLocalizer.LocalizeStepName(_rawCurrentStepName);
            CurrentStepProgressText = DeploymentUiTextLocalizer.LocalizeMessage(_rawCurrentStepProgressText);
            FailedStepName = string.IsNullOrWhiteSpace(_rawFailedStepName)
                ? string.Empty
                : DeploymentUiTextLocalizer.LocalizeStepName(_rawFailedStepName);
            FailedStepErrorMessage = string.IsNullOrWhiteSpace(_rawFailedStepErrorMessage)
                ? string.Empty
                : DeploymentUiTextLocalizer.LocalizeMessage(_rawFailedStepErrorMessage);
            StepCounterText = BuildStepCounterText(_activeStepIndex);
            OnPropertyChanged(nameof(RebootCountdownText));
            CaptureNetworkSnapshot();
        });
    }

    private string GetString(string key)
    {
        return Strings[key];
    }

    private string Format(string key, params object[] args)
    {
        return string.Format(LocalizationService.CurrentCulture, GetString(key), args);
    }
}

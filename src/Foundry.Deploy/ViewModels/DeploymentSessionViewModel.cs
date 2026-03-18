using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentSessionViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IProcessRunner _processRunner;
    private readonly bool _isDebugSafeMode;
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
        bool isDebugSafeMode)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationProgressService = operationProgressService ?? throw new ArgumentNullException(nameof(operationProgressService));
        _deploymentOrchestrator = deploymentOrchestrator ?? throw new ArgumentNullException(nameof(deploymentOrchestrator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _isDebugSafeMode = isDebugSafeMode;

        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged += OnStepProgressChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWizardPage))]
    [NotifyPropertyChangedFor(nameof(IsProgressPage))]
    [NotifyPropertyChangedFor(nameof(IsSuccessPage))]
    [NotifyPropertyChangedFor(nameof(IsErrorPage))]
    private DeploymentPage currentPage = DeploymentPage.Wizard;

    [ObservableProperty]
    private string deploymentStatus = "Ready";

    [ObservableProperty]
    private int deploymentProgress;

    [ObservableProperty]
    private bool isGlobalProgressIndeterminate = true;

    [ObservableProperty]
    private string globalProgressPercentText = "0%";

    [ObservableProperty]
    private string currentStepName = "Waiting for deployment...";

    [ObservableProperty]
    private string stepCounterText = "Step: ? of ?";

    [ObservableProperty]
    private double currentStepProgress;

    [ObservableProperty]
    private bool isCurrentStepProgressIndeterminate = true;

    [ObservableProperty]
    private string currentStepProgressText = "Waiting for progress...";

    [ObservableProperty]
    private string computerNameText = string.Empty;

    [ObservableProperty]
    private string ipAddress = "N/A";

    [ObservableProperty]
    private string subnetMask = "N/A";

    [ObservableProperty]
    private string gatewayAddress = "N/A";

    [ObservableProperty]
    private string macAddress = "N/A";

    [ObservableProperty]
    private string startTimeText = "N/A";

    [ObservableProperty]
    private string elapsedTimeText = "00:00:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebootNowCommand))]
    private int rebootCountdownSeconds = 10;

    [ObservableProperty]
    private string failedStepName = string.Empty;

    [ObservableProperty]
    private string failedStepErrorMessage = string.Empty;

    public bool IsWizardPage => CurrentPage == DeploymentPage.Wizard;

    public bool IsProgressPage => CurrentPage == DeploymentPage.Progress;

    public bool IsSuccessPage => CurrentPage == DeploymentPage.Success;

    public bool IsErrorPage => CurrentPage == DeploymentPage.Error;

    public int PlannedStepCount => _deploymentOrchestrator.PlannedSteps.Count;

    public void SetStatus(string status)
    {
        DeploymentStatus = status;
    }

    public void SetComputerName(string computerName)
    {
        ComputerNameText = computerName;
    }

    public void ResetToWizard(string? status = null)
    {
        _isDeploymentInProgress = false;
        _lastLogsDirectoryPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(status))
        {
            DeploymentStatus = status;
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
        CurrentStepName = "Preparing deployment...";
        CurrentStepProgress = 0;
        IsCurrentStepProgressIndeterminate = true;
        CurrentStepProgressText = "Waiting for progress...";
        StepCounterText = BuildStepCounterText(0);
        ComputerNameText = computerName;
        CaptureNetworkSnapshot();

        _deploymentStartTimeUtc = DateTimeOffset.Now;
        StartTimeText = _deploymentStartTimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ElapsedTimeText = "00:00:00";
        StartElapsedTimeTracking();

        DeploymentStatus = "Deployment started.";
        CurrentPage = DeploymentPage.Progress;
    }

    public void CompleteDeployment(string status, string? logsDirectoryPath)
    {
        _isDeploymentInProgress = false;
        _lastLogsDirectoryPath = logsDirectoryPath ?? string.Empty;
        DeploymentStatus = status;
        CurrentPage = DeploymentPage.Success;
    }

    public void FailDeployment(string status, string? stepName, string? errorMessage, string? logsDirectoryPath = null)
    {
        _isDeploymentInProgress = false;
        _lastLogsDirectoryPath = logsDirectoryPath ?? _lastLogsDirectoryPath;
        SetFailureDetails(stepName, errorMessage);
        DeploymentStatus = status;
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
        CurrentStepName = currentStepName;
        StepCounterText = BuildStepCounterText(currentStepIndex);
        CurrentStepProgress = 65;
        IsCurrentStepProgressIndeterminate = false;
        CurrentStepProgressText = "Applying image: 65%";
        CaptureNetworkSnapshot();
        _deploymentStartTimeUtc = DateTimeOffset.Now;
        StartTimeText = _deploymentStartTimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ElapsedTimeText = "00:00:00";
        StartElapsedTimeTracking();
        DeploymentStatus = "Debug preview: progress page.";
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
        CurrentStepName = finalStepName;
        StepCounterText = BuildStepCounterText(plannedStepCount);
        CurrentStepProgress = 100;
        IsCurrentStepProgressIndeterminate = false;
        CurrentStepProgressText = "Step completed.";
        DeploymentStatus = "Debug preview: success page.";
        CurrentPage = DeploymentPage.Success;
    }

    public void ShowDebugError(string computerName, int currentStepIndex, string failedStepName, string failedStepErrorMessage)
    {
        _isDeploymentInProgress = false;
        StopElapsedTimeTracking();
        ComputerNameText = computerName;
        StepCounterText = BuildStepCounterText(currentStepIndex);
        SetFailureDetails(failedStepName, failedStepErrorMessage);
        DeploymentStatus = "Debug preview: error page.";
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
            DeploymentStatus = $"Unable to open log file: {ex.Message}";
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
                DeploymentStatus = _operationProgressService.Status!;
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
                CurrentStepProgressText = "Starting step...";
            }

            CurrentStepName = stepProgress.StepName;
            StepCounterText = $"Step: {stepProgress.StepIndex} of {stepProgress.StepCount}";

            DeploymentProgress = Math.Max(DeploymentProgress, stepProgress.ProgressPercent);
            UpdateGlobalProgressVisuals(DeploymentProgress);
            UpdateCurrentStepProgressVisuals(stepProgress);

            if (!string.IsNullOrWhiteSpace(stepProgress.Message))
            {
                DeploymentStatus = stepProgress.Message;
            }

            if (stepProgress.State == DeploymentStepState.Failed)
            {
                SetFailureDetails(stepProgress.StepName, stepProgress.Message ?? "Step failed.");
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged -= OnStepProgressChanged;
        StopElapsedTimeTracking();
        StopRebootCountdown(resetSeconds: false);
        _isDisposed = true;
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
            CurrentStepProgressText = stepProgress.StepSubProgressLabel ?? "Step completed.";
            return;
        }

        if (stepProgress.State == DeploymentStepState.Failed)
        {
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = stepProgress.Message ?? "Step failed.";
            return;
        }

        if (stepProgress.State == DeploymentStepState.Skipped)
        {
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = stepProgress.Message ?? "Step skipped.";
            return;
        }

        if (stepProgress.StepSubProgressPercent.HasValue)
        {
            double normalized = Math.Clamp(stepProgress.StepSubProgressPercent.Value, 0d, 100d);
            CurrentStepProgress = normalized;
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = string.IsNullOrWhiteSpace(stepProgress.StepSubProgressLabel)
                ? $"{normalized:0.#}%"
                : stepProgress.StepSubProgressLabel!;
            return;
        }

        if (stepProgress.StepSubProgressIndeterminate)
        {
            IsCurrentStepProgressIndeterminate = true;
            CurrentStepProgressText = stepProgress.StepSubProgressLabel ?? "In progress...";
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

            DeploymentStatus = "Rebooting now...";

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
                DeploymentStatus = "Reboot command failed.";
                CurrentPage = DeploymentPage.Error;
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                SetFailureDetails("System reboot", ex.Message);
                DeploymentStatus = $"Reboot command failed: {ex.Message}";
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
        FailedStepName = string.IsNullOrWhiteSpace(stepName) ? "Unknown step" : stepName;
        FailedStepErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "No error details were provided." : errorMessage;
    }

    private void ClearFailureDetails()
    {
        FailedStepName = string.Empty;
        FailedStepErrorMessage = string.Empty;
    }

    private void CaptureNetworkSnapshot()
    {
        IpAddress = "N/A";
        SubnetMask = "N/A";
        GatewayAddress = "N/A";
        MacAddress = "N/A";

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
                SubnetMask = ipv4AddressInfo.IPv4Mask?.ToString() ?? "N/A";
                GatewayAddress = gatewayInfo?.Address.ToString() ?? "N/A";
                MacAddress = macBytes.Length == 0
                    ? "N/A"
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
            return "Step: ? of ?";
        }

        int normalizedStep = Math.Clamp(currentStep, 0, _plannedStepCount);
        return $"Step: {normalizedStep} of {_plannedStepCount}";
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
}

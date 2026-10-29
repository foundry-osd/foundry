// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Services.Logging;
using Foundry.Wpf.Localization;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Runtime;
using Foundry.Utilities.Storage;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Tools menu diagnostics: opens the current log file and exports a support bundle.
/// </summary>
/// <remarks>
/// The export is sanitized by default; the raw export asks for an explicit sensitive-data confirmation first. The
/// dialogs and the folder picker are shown from here, as before the page rework (L3-16). Disposing the view model
/// cancels a running export.
/// </remarks>
public sealed partial class DiagnosticsViewModel : LocalizedViewModelBase
{
    private const int ShellFolderPickerUnavailableHResult = unchecked((int)0x80040111);

    private readonly ILogger<DiagnosticsViewModel> _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _isDisposed;

    /// <summary>
    /// Initializes the diagnostics commands.
    /// </summary>
    /// <param name="localization">Localization service for the dialog texts.</param>
    /// <param name="logger">Logger for the diagnostics operations.</param>
    public DiagnosticsViewModel(ILocalizationService localization, ILogger<DiagnosticsViewModel> logger)
        : base(localization)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Cancels a running export and releases the view model.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        base.Dispose();
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        string logFilePath = FoundryConnectLogging.CurrentLogFilePath;
        if (!File.Exists(logFilePath))
        {
            _logger.LogWarning("The Foundry Connect log file is unavailable. LogFilePath={LogFilePath}", logFilePath);
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new("notepad.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(logFilePath);
            _ = Process.Start(startInfo);
            _logger.LogInformation("Opened log file at {LogFilePath}.", logFilePath);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _logger.LogError("Failed to open log file. LogFilePath={LogFilePath}, FailureType={FailureType}", logFilePath, exception.GetType().Name);
        }
    }

    [RelayCommand]
    private Task ExportDiagnosticsAsync()
    {
        return ExportDiagnosticsAsync(SupportBundlePrivacyMode.Sanitized);
    }

    [RelayCommand]
    private async Task ExportRawDiagnosticsAsync()
    {
        var confirmationDialog = new LocalizedMessageDialog(
            Strings["Diagnostics.ExportRawTitle"],
            Strings["Diagnostics.ExportRawWarning"],
            Strings["Diagnostics.ExportRawConfirm"],
            Strings["Common.Cancel"]);
        if (confirmationDialog.ShowDialog() != true)
        {
            return;
        }

        await ExportDiagnosticsAsync(SupportBundlePrivacyMode.Raw);
    }

    private async Task ExportDiagnosticsAsync(SupportBundlePrivacyMode privacyMode)
    {
        if (_isDisposed)
        {
            return;
        }

        // Read once before the first await: Dispose disposes the source.
        CancellationToken cancellationToken = _disposeCts.Token;

        try
        {
            string? destinationDirectoryPath = SelectExportDestination();
            if (destinationDirectoryPath is null)
            {
                return;
            }

            string? logDirectoryPath = Path.GetDirectoryName(FoundryConnectLogging.CurrentLogFilePath);
            string[] logFilePaths = string.IsNullOrWhiteSpace(logDirectoryPath) || !Directory.Exists(logDirectoryPath)
                ? []
                : Directory.GetFiles(logDirectoryPath, "Foundry*.log", SearchOption.TopDirectoryOnly);

            _logger.LogInformation(
                "Support bundle export started. PrivacyMode={PrivacyMode}, LogFileCount={LogFileCount}",
                privacyMode,
                logFilePaths.Length);

            SupportBundleResult result = await new SupportBundleExporter().ExportAsync(
                new SupportBundleRequest
                {
                    ApplicationName = "Foundry.Connect",
                    ApplicationVersion = FoundryConnectApplicationInfo.Version,
                    SessionId = DiagnosticSessionContext.CurrentSessionId,
                    DestinationDirectoryPath = destinationDirectoryPath,
                    LogFilePaths = logFilePaths,
                    PrivacyMode = privacyMode,
                    Summary = new Dictionary<string, string>
                    {
                        ["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                        ["DeploymentMode"] = Environment.GetEnvironmentVariable("FOUNDRY_DEPLOYMENT_MODE") ?? "Unknown",
                        ["OperatingSystem"] = Environment.OSVersion.VersionString
                    }
                },
                cancellationToken);

            _logger.LogInformation(
                "Support bundle export completed. PrivacyMode={PrivacyMode}, IncludedFileCount={IncludedFileCount}, OmittedFileCount={OmittedFileCount}",
                privacyMode,
                result.IncludedFiles.Count,
                result.OmittedFiles.Count);
            _ = new LocalizedMessageDialog(
                Strings["Diagnostics.ExportSucceededTitle"],
                string.Format(LocalizationService.CurrentCulture, Strings["Diagnostics.ExportSucceededMessageFormat"], result.ArchivePath),
                Strings["Common.Close"]).ShowDialog();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Support bundle export canceled because the application is shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Support bundle export failed. PrivacyMode={PrivacyMode}, FailureType={FailureType}", privacyMode, ex.GetType().Name);
            _ = new LocalizedMessageDialog(
                Strings["Diagnostics.ExportFailedTitle"],
                Strings["Diagnostics.ExportFailedMessage"],
                Strings["Common.Close"]).ShowDialog();
        }
    }

    private string? SelectExportDestination()
    {
        if (WinPeRuntimeDetector.IsWinPeRuntime())
        {
            _logger.LogInformation("The shell folder picker was skipped because the application is running in WinPE.");
            return ResolveRequiredExternalExportDirectory();
        }

        try
        {
            string? externalExportDirectory = ResolveExternalExportDirectory();
            var picker = new OpenFolderDialog
            {
                Title = Strings["Diagnostics.ExportDestinationTitle"],
                InitialDirectory = ResolveSuggestedExportDirectory(externalExportDirectory)
            };
            return picker.ShowDialog() == true ? picker.FolderName : null;
        }
        catch (COMException ex) when (ex.HResult == ShellFolderPickerUnavailableHResult)
        {
            string? fallbackDirectoryPath = ResolveExternalExportDirectory();
            _logger.LogWarning(
                "The shell folder picker is unavailable. Falling back to an external volume. HasFallbackDestination={HasFallbackDestination}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                fallbackDirectoryPath is not null,
                ex.GetType().Name,
                ex.HResult);
            return fallbackDirectoryPath
                   ?? throw new IOException("No ready external volume is available for the diagnostic export.");
        }
    }

    private static string ResolveRequiredExternalExportDirectory()
    {
        return ResolveExternalExportDirectory()
               ?? throw new IOException("No ready external volume is available for the diagnostic export.");
    }

    private static string ResolveSuggestedExportDirectory(string? externalExportDirectory)
    {
        try
        {
            return externalExportDirectory
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
        catch
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
    }

    private static string? ResolveExternalExportDirectory()
    {
        return SupportBundleDestinationPolicy.SelectExternalDestination(
            new WindowsVolumeDiscovery().GetVolumes());
    }
}

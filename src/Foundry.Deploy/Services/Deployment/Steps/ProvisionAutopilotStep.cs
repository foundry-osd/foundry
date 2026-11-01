// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Runs the selected late Autopilot provisioning path after the offline Windows image has been applied.
/// </summary>
public sealed class ProvisionAutopilotStep : DeploymentStepBase
{
    private const string TargetConfigurationRelativePath = @"Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json";
    private const string HardwareHashDiagnosticsFolderName = "AutopilotHash";
    private const string HardwareHashStatusFileName = "autopilot-hash-upload-status.json";
    private const string WinPeWindowsFolderName = "Windows";

    private readonly IAutopilotHardwareHashCaptureService _hardwareHashCaptureService;
    private readonly IAutopilotHardwareHashUploadService _hardwareHashUploadService;
    private readonly IAutopilotInteractiveProvisioningService _interactiveProvisioningService;
    private readonly IAutopilotProfileContentService _autopilotProfileContentService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IKeyboardLayoutSwitcher _keyboardLayoutSwitcher;

    /// <summary>Creates the step.</summary>
    /// <param name="hardwareHashCaptureService">Captures the hardware hash for zero-touch upload.</param>
    /// <param name="hardwareHashUploadService">Uploads the hardware hash for zero-touch upload.</param>
    /// <param name="interactiveProvisioningService">Installs and configures Foundry.AutopilotInteractive in the target OS.</param>
    /// <param name="autopilotProfileContentService">Reads the selected offline Autopilot profile.</param>
    /// <param name="themeService">Supplies the theme Foundry.AutopilotInteractive starts with.</param>
    /// <param name="localizationService">Supplies the UI culture Foundry.AutopilotInteractive starts with.</param>
    /// <param name="keyboardLayoutSwitcher">Supplies the keyboard layout Foundry.AutopilotInteractive starts with.</param>
    public ProvisionAutopilotStep(
        IAutopilotHardwareHashCaptureService hardwareHashCaptureService,
        IAutopilotHardwareHashUploadService hardwareHashUploadService,
        IAutopilotInteractiveProvisioningService interactiveProvisioningService,
        IAutopilotProfileContentService autopilotProfileContentService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher)
    {
        _hardwareHashCaptureService = hardwareHashCaptureService;
        _hardwareHashUploadService = hardwareHashUploadService;
        _interactiveProvisioningService = interactiveProvisioningService;
        _autopilotProfileContentService = autopilotProfileContentService;
        _themeService = themeService;
        _localizationService = localizationService;
        _keyboardLayoutSwitcher = keyboardLayoutSwitcher;
    }

    public override string Name => DeploymentStepNames.ProvisionAutopilot;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        context.RuntimeState.AutopilotProvisioningMode = context.Request.AutopilotProvisioningMode;
        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
        {
            return await PrepareHardwareHashUploadAsync(context, dryRun: false, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.InteractiveHardwareHashUpload)
        {
            return await StageInteractiveHardwareHashUploadAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return CreateMissingProfileFailure();
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target Windows partition is unavailable for Autopilot staging.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageAutopilotProfile,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_partition"));
        }

        string sourceConfigurationPath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath;
        if (!File.Exists(sourceConfigurationPath))
        {
            return DeploymentStepResult.Failed(
                $"Selected Autopilot profile file was not found: '{sourceConfigurationPath}'.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageAutopilotProfile,
                    DeploymentFailureReasons.NotFound,
                    "autopilot_profile_file_not_found"));
        }

        string targetConfigurationPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            TargetConfigurationRelativePath);
        string? targetDirectoryPath = Path.GetDirectoryName(targetConfigurationPath);
        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            return DeploymentStepResult.Failed(
                "Failed to resolve the target Autopilot directory.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageAutopilotProfile,
                    DeploymentFailureReasons.InvalidState,
                    "unresolved_autopilot_target_directory"));
        }

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Copying AutopilotConfigurationFile.json...", DeploymentOperationNames.StageAutopilotProfile);
        Directory.CreateDirectory(targetDirectoryPath);
        byte[] profileContent = await _autopilotProfileContentService
            .ReadAsync(context.Request.SelectedAutopilotProfile, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await File.WriteAllBytesAsync(targetConfigurationPath, profileContent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(profileContent);
        }

        context.RuntimeState.StagedAutopilotConfigurationPath = targetConfigurationPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot profile '{context.Request.SelectedAutopilotProfile.DisplayName}' staged to '{targetConfigurationPath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        context.RuntimeState.AutopilotProvisioningMode = context.Request.AutopilotProvisioningMode;
        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
        {
            return await PrepareHardwareHashUploadAsync(context, dryRun: true, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.InteractiveHardwareHashUpload)
        {
            return await WriteDryRunInteractiveRegistrationManifestAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return CreateMissingProfileFailure();
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string autopilotRoot = Path.Combine(targetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);

        string manifestPath = Path.Combine(autopilotRoot, "autopilot-profile-stage.dryrun.json");
        string manifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            selectedProfileFolderName = context.Request.SelectedAutopilotProfile.FolderName,
            selectedProfileDisplayName = context.Request.SelectedAutopilotProfile.DisplayName,
            sourceConfigurationFilePath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath,
            targetConfigurationRelativePath = TargetConfigurationRelativePath
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Writing dry-run Autopilot manifest...", DeploymentOperationNames.WriteAutopilotManifest);
        await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = manifestPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Autopilot profile staging simulated: {manifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged (simulation).");
    }

    private async Task<DeploymentStepResult> StageInteractiveHardwareHashUploadAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target Windows partition is unavailable for interactive Autopilot registration assistant staging.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageAutopilotAssistant,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_partition"));
        }

        context.EmitCurrentStepIndeterminate("Staging Autopilot registration assistant...", "Copying interactive registration files...", DeploymentOperationNames.StageAutopilotAssistant);
        string? assignedComputerName = await ResolveAssignedComputerNameAsync(context, cancellationToken).ConfigureAwait(false);
        AutopilotInteractiveRuntimeConfiguration configuration = CreateInteractiveConfiguration(context, assignedComputerName);
        AutopilotInteractiveProvisioningResult provisioningResult;
        try
        {
            // The preflight resolves the archive before any disk operation; a missing origin means it did not run for this request.
            AutopilotInteractiveRuntimeOrigin origin = context.AutopilotInteractiveRuntime
                ?? throw new InvalidOperationException("The Foundry.AutopilotInteractive runtime was not resolved before disk preparation.");
            IProgress<DownloadProgress> progress = context.CreateDownloadProgressReporter(
                AutopilotInteractiveLayout.ApplicationName, DeploymentOperationNames.StageAutopilotAssistant);
            provisioningResult = await _interactiveProvisioningService.ProvisionAsync(
                context.RuntimeState.TargetWindowsPartitionRoot, origin, configuration, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Error,
                $"Interactive Autopilot registration assistant installation failed. FailureType={exception.GetType().Name}.",
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Failed(
                LocalizationText.GetString("Autopilot.InteractiveInstallFailed"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.StageAutopilotAssistant) with
                {
                    OperationName = DeploymentOperationNames.StageAutopilotAssistant,
                    Code = "autopilot_interactive_install_failed"
                });
        }

        context.RuntimeState.StagedAutopilotConfigurationPath = provisioningResult.ConfigPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Interactive Autopilot registration assistant installed to '{provisioningResult.RuntimeDirectory}'.",
            cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Interactive Autopilot registration assistant staged.");
    }

    /// <summary>
    /// Builds the configuration Foundry.AutopilotInteractive reads at startup: the computer name to assign (upload option only), the
    /// display-only final computer name, the interactive options,
    /// the theme, culture and keyboard layout currently active in Deploy (the layout chosen in Connect or in Deploy's Keyboard
    /// menu, otherwise the WinPE layout), and the diagnostic session that correlates its logs with this deployment.
    /// </summary>
    private AutopilotInteractiveRuntimeConfiguration CreateInteractiveConfiguration(DeploymentStepExecutionContext context, string? assignedComputerName)
    {
        string cultureName = _localizationService.CurrentCulture.Name;
        return new AutopilotInteractiveRuntimeConfiguration
        {
            AssignedComputerName = assignedComputerName,
            ComputerName = ResolveFinalComputerName(context),
            PreselectCustomGroupTag = context.Request.AutopilotInteractive.PreselectCustomGroupTag,
            OfferAssignedUser = context.Request.AutopilotInteractive.OfferAssignedUser,
            Theme = ToRuntimeTheme(_themeService.CurrentTheme),
            CultureName = string.IsNullOrWhiteSpace(cultureName) ? "en-US" : cultureName,
            DiagnosticSessionId = DiagnosticSessionContext.CurrentSessionId,
            KeyboardLayoutId = KeyboardLayoutCatalog.TryNormalizeId(_keyboardLayoutSwitcher.GetActiveLayoutId(), out string layoutId)
                ? layoutId
                : null
        };
    }

    private static RuntimeThemePreference ToRuntimeTheme(ThemeMode theme) => theme switch
    {
        ThemeMode.Light => RuntimeThemePreference.Light,
        ThemeMode.Dark => RuntimeThemePreference.Dark,
        _ => RuntimeThemePreference.System
    };

    private async Task<DeploymentStepResult> PrepareHardwareHashUploadAsync(
        DeploymentStepExecutionContext context,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        DeployAutopilotHardwareHashUploadSettings settings = context.Request.AutopilotHardwareHashUpload;
        context.RuntimeState.AutopilotHardwareHashGroupTag = NormalizeGroupTag(settings.DefaultGroupTag);
        context.RuntimeState.StagedAutopilotConfigurationPath = null;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.Planned;

        if (!dryRun && string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target Windows partition is unavailable for Autopilot hardware hash upload.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.CaptureAutopilotHash,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_partition"));
        }

        if (settings.ActiveCertificateExpiresOnUtc is DateTimeOffset expiresOn &&
            expiresOn <= DateTimeOffset.UtcNow)
        {
            const string message = "Autopilot hardware hash upload skipped because the embedded certificate is expired.";
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.SkippedCertificateExpired,
                message,
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped(message);
        }

        if (!HasRequiredHardwareHashMetadata(settings))
        {
            const string message = "Autopilot hardware hash upload skipped because media metadata is incomplete.";
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.SkippedMissingConfiguration,
                message,
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped(message);
        }

        if (dryRun)
        {
            return await WriteDryRunHardwareHashManifestAsync(context, cancellationToken).ConfigureAwait(false);
        }

        string diagnosticsPath = ResolveHardwareHashDiagnosticsPath(context);
        context.EmitCurrentStepIndeterminate("Capturing Autopilot hardware hash...", "Running OA3Tool...", DeploymentOperationNames.CaptureAutopilotHash);
        AutopilotHardwareHashCaptureResult captureResult = await _hardwareHashCaptureService
            .CaptureAsync(
                new AutopilotHardwareHashCaptureRequest
                {
                    TargetWindowsRootPath = context.RuntimeState.TargetWindowsPartitionRoot!,
                    WinPeWindowsRootPath = ResolveWinPeWindowsRoot(context.RuntimeState.WorkspaceRoot),
                    WorkspaceRootPath = context.RuntimeState.WorkspaceRoot,
                    DiagnosticsRootPath = diagnosticsPath,
                    GroupTag = context.RuntimeState.AutopilotHardwareHashGroupTag
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!captureResult.IsSuccess)
        {
            string failureMessage = $"Autopilot hardware hash capture failed: {captureResult.Message}";
            DeploymentStepResult stepResult = IsBlockingHardwareHashCaptureFailure(captureResult.FailureCode)
                ? DeploymentStepResult.Failed(
                    failureMessage,
                    CreateHardwareHashCaptureFailure(captureResult.FailureCode))
                : DeploymentStepResult.Skipped(failureMessage);
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.CaptureFailed,
                failureMessage,
                cancellationToken,
                captureResult.FailureCode.ToString()).ConfigureAwait(false);
            return stepResult;
        }

        string? assignedComputerName = await ResolveAssignedComputerNameAsync(context, cancellationToken).ConfigureAwait(false);
        context.EmitCurrentStepIndeterminate("Uploading Autopilot hardware hash...", "Preparing Microsoft Graph import...", DeploymentOperationNames.UploadAutopilotHash);
        AutopilotHardwareHashUploadResult uploadResult = await _hardwareHashUploadService.UploadAsync(
            new AutopilotHardwareHashUploadRequest
            {
                Settings = settings,
                Identity = captureResult.Identity!,
                AssignedComputerName = assignedComputerName,
                WorkspaceRootPath = context.RuntimeState.WorkspaceRoot,
                DiagnosticsRootPath = diagnosticsPath
            },
            new Progress<AutopilotHardwareHashUploadProgress>(progress =>
            {
                context.EmitCurrentStep(
                    DeploymentStepState.Running,
                    progress.Message,
                    stepSubProgressPercent: null,
                    stepSubProgressIndeterminate: progress.IsIndeterminate,
                    stepSubProgressLabel: progress.Detail);
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteHardwareHashStatusAsync(
            context,
            uploadResult.State,
            uploadResult.Message,
            cancellationToken,
            uploadResult.FailureCode,
            captureResult,
            uploadResult).ConfigureAwait(false);
        await context.AppendLogAsync(
            uploadResult.IsCompleted ? DeploymentLogLevel.Info : DeploymentLogLevel.Warning,
            $"Autopilot hardware hash upload state '{uploadResult.State}'. DiagnosticsPath='{context.RuntimeState.AutopilotHardwareHashDiagnosticsPath}', GroupTagConfigured={!string.IsNullOrWhiteSpace(context.RuntimeState.AutopilotHardwareHashGroupTag)}.",
            cancellationToken).ConfigureAwait(false);

        return uploadResult.IsCompleted
            ? DeploymentStepResult.Succeeded(uploadResult.Message)
            : DeploymentStepResult.Skipped(uploadResult.Message);
    }

    /// <summary>
    /// Resolves the name sent to Autopilot: the final computer name, only when the upload option is enabled.
    /// </summary>
    private static async Task<string?> ResolveAssignedComputerNameAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.UploadComputerNameToAutopilot)
        {
            return null;
        }

        string? finalComputerName = ResolveFinalComputerName(context);
        if (finalComputerName is null)
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Warning,
                "Autopilot computer name assignment skipped because no final Foundry Deploy computer name is available. Hardware hash upload continues.",
                cancellationToken).ConfigureAwait(false);
        }

        return finalComputerName;
    }

    /// <summary>
    /// Returns the deployment's final computer name when it is known and reliable, independent of the upload option.
    /// A custom unattend may rename the device, so no name is reliable then; an invalid target name is never reported.
    /// </summary>
    private static string? ResolveFinalComputerName(DeploymentStepExecutionContext context)
    {
        return !context.Request.UsesCustomUnattend &&
            Foundry.Core.Services.Configuration.ComputerNameRules.IsValid(context.Request.TargetComputerName)
            ? context.Request.TargetComputerName
            : null;
    }

    private static async Task<DeploymentStepResult> WriteDryRunHardwareHashManifestAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        string hashUploadTargetFoundryRoot = context.EnsureTargetFoundryRoot();
        string hashUploadAutopilotRoot = Path.Combine(hashUploadTargetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(hashUploadAutopilotRoot);

        string hashUploadManifestPath = Path.Combine(hashUploadAutopilotRoot, "autopilot-hash-upload.dryrun.json");
        string hashUploadManifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            provisioningMode = "hardwareHashUpload",
            tenantId = context.Request.AutopilotHardwareHashUpload.TenantId,
            clientId = context.Request.AutopilotHardwareHashUpload.ClientId,
            activeCertificateThumbprint = context.Request.AutopilotHardwareHashUpload.ActiveCertificateThumbprint,
            activeCertificateExpiresOnUtc = context.Request.AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc,
            groupTag = context.RuntimeState.AutopilotHardwareHashGroupTag,
            uploadState = AutopilotHardwareHashUploadState.DryRunPrepared.ToString()
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Preparing Autopilot hardware hash upload...", "Writing dry-run Autopilot hash manifest...", DeploymentOperationNames.WriteAutopilotManifest);
        await File.WriteAllTextAsync(hashUploadManifestPath, hashUploadManifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = hashUploadManifestPath;
        await WriteHardwareHashStatusAsync(
            context,
            AutopilotHardwareHashUploadState.DryRunPrepared,
            "Autopilot hardware hash upload prepared for dry run.",
            cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Autopilot hardware hash upload simulated: {hashUploadManifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot hardware hash upload prepared (simulation).");
    }

    private static async Task<DeploymentStepResult> WriteDryRunInteractiveRegistrationManifestAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        string interactiveTargetFoundryRoot = context.EnsureTargetFoundryRoot();
        string interactiveAutopilotRoot = Path.Combine(interactiveTargetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(interactiveAutopilotRoot);

        string manifestPath = Path.Combine(interactiveAutopilotRoot, "interactive-registration.dryrun.json");
        string manifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            provisioningMode = "interactiveHardwareHashUpload",
            executableRelativePath = AutopilotInteractiveProvisioningService.RuntimeExecutableRelativePath,
            configRelativePath = AutopilotInteractiveProvisioningService.RuntimeConfigRelativePath
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Staging Autopilot registration assistant...", "Writing dry-run interactive registration manifest...", DeploymentOperationNames.WriteAutopilotManifest);
        await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = manifestPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Interactive Autopilot registration assistant staging simulated: {manifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Interactive Autopilot registration assistant staged (simulation).");
    }

    private static async Task WriteHardwareHashStatusAsync(
        DeploymentStepExecutionContext context,
        AutopilotHardwareHashUploadState state,
        string message,
        CancellationToken cancellationToken,
        string? failureCode = null,
        AutopilotHardwareHashCaptureResult? captureResult = null,
        AutopilotHardwareHashUploadResult? uploadResult = null)
    {
        string diagnosticsPath = ResolveHardwareHashDiagnosticsPath(context);
        AutopilotDiagnosticsDirectory.CreateRestricted(diagnosticsPath);
        context.RuntimeState.AutopilotHardwareHashUploadState = state;
        context.RuntimeState.AutopilotHardwareHashUploadMessage = message;
        context.RuntimeState.AutopilotHardwareHashDiagnosticsPath = diagnosticsPath;

        string statusPath = Path.Combine(diagnosticsPath, HardwareHashStatusFileName);
        string statusJson = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            provisioningMode = "hardwareHashUpload",
            uploadState = state.ToString(),
            message,
            failureCode,
            groupTag = context.RuntimeState.AutopilotHardwareHashGroupTag,
            serialNumber = captureResult?.Identity?.SerialNumber,
            oa3XmlPath = captureResult?.Oa3XmlPath,
            oa3LogPath = captureResult?.Oa3LogPath,
            autopilotHwidCsvPath = captureResult?.CsvPath,
            autopilotUploadResultPath = uploadResult?.ArtifactPath,
            importId = uploadResult?.ImportId,
            importedIdentityId = uploadResult?.ImportedIdentityId,
            autopilotDeviceId = uploadResult?.AutopilotDeviceId,
            activeCertificateThumbprint = context.Request.AutopilotHardwareHashUpload.ActiveCertificateThumbprint,
            activeCertificateExpiresOnUtc = context.Request.AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(statusPath, statusJson, cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot hardware hash upload state: {state}. DiagnosticsPath='{diagnosticsPath}'.",
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveHardwareHashDiagnosticsPath(DeploymentStepExecutionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return Path.Combine(
                context.RuntimeState.TargetWindowsPartitionRoot,
                "Windows",
                "Temp",
                "Foundry",
                "Logs",
                HardwareHashDiagnosticsFolderName);
        }

        return Path.Combine(context.EnsureTargetFoundryRoot(), "Logs", HardwareHashDiagnosticsFolderName);
    }

    private static string ResolveWinPeWindowsRoot(string workspaceRoot)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string? driveRoot = Path.GetPathRoot(fullWorkspaceRoot);
        return string.IsNullOrWhiteSpace(driveRoot)
            ? Path.Combine(fullWorkspaceRoot, "..", WinPeWindowsFolderName)
            : Path.Combine(driveRoot, WinPeWindowsFolderName);
    }

    private static bool HasRequiredHardwareHashMetadata(DeployAutopilotHardwareHashUploadSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.TenantId) &&
               !string.IsNullOrWhiteSpace(settings.ClientId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificateKeyId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificateThumbprint) &&
               settings.ActiveCertificateExpiresOnUtc is not null;
    }

    private static string? NormalizeGroupTag(string? groupTag)
    {
        return string.IsNullOrWhiteSpace(groupTag)
            ? null
            : groupTag.Trim();
    }

    private static bool IsBlockingHardwareHashCaptureFailure(AutopilotHardwareHashCaptureFailureCode failureCode)
    {
        return failureCode is AutopilotHardwareHashCaptureFailureCode.SupportLibraryMissing
            or AutopilotHardwareHashCaptureFailureCode.SupportLibraryCopyFailed
            or AutopilotHardwareHashCaptureFailureCode.SupportLibraryLoadFailed
            or AutopilotHardwareHashCaptureFailureCode.SupportLibraryRegistrationFailed;
    }

    private static DeploymentFailure CreateHardwareHashCaptureFailure(
        AutopilotHardwareHashCaptureFailureCode failureCode)
    {
        return failureCode switch
        {
            AutopilotHardwareHashCaptureFailureCode.SupportLibraryMissing => new(
                DeploymentOperationNames.CaptureAutopilotHash,
                DeploymentFailureKinds.Validation,
                DeploymentFailureReasons.MissingResource,
                "support_library_missing"),
            AutopilotHardwareHashCaptureFailureCode.SupportLibraryCopyFailed => new(
                DeploymentOperationNames.CaptureAutopilotHash,
                DeploymentFailureKinds.Io,
                DeploymentFailureReasons.UnexpectedException,
                "support_library_copy_failed"),
            AutopilotHardwareHashCaptureFailureCode.SupportLibraryRegistrationFailed => new(
                DeploymentOperationNames.CaptureAutopilotHash,
                DeploymentFailureKinds.Process,
                DeploymentFailureReasons.NonZeroExit,
                "support_library_registration_failed"),
            _ => new(
                DeploymentOperationNames.CaptureAutopilotHash,
                DeploymentFailureKinds.Process,
                DeploymentFailureReasons.StartFailed,
                "support_library_load_failed")
        };
    }

    private static DeploymentStepResult CreateMissingProfileFailure() =>
        DeploymentStepResult.Failed(
            "Autopilot is enabled but no profile was selected.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.ProvisionAutopilot,
                DeploymentFailureReasons.MissingResource,
                "missing_autopilot_profile"));

}

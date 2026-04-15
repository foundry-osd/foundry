using Foundry.Services.Operations;
using Foundry.Models.Configuration;
using Foundry.Services.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class MediaOutputService : IMediaOutputService
{
    private readonly IOperationProgressService _operationProgressService;
    private readonly IWinPeBuildService _buildService;
    private readonly IWinPeWorkspacePreparationService _workspacePreparationService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly WinPeProcessRunner _processRunner;
    private readonly WinPeUsbMediaService _usbMediaService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<MediaOutputService> _logger;

    public MediaOutputService(
        IOperationProgressService operationProgressService,
        IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService workspacePreparationService,
        WinPeToolResolver toolResolver,
        WinPeProcessRunner processRunner,
        WinPeUsbMediaService usbMediaService,
        ILocalizationService localizationService,
        ILogger<MediaOutputService> logger)
    {
        _operationProgressService = operationProgressService;
        _buildService = buildService;
        _workspacePreparationService = workspacePreparationService;
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _usbMediaService = usbMediaService;
        _localizationService = localizationService;
        _logger = logger;
    }

    public WinPeResult<IReadOnlyList<string>> GetAvailableWinPeLanguages(
        WinPeArchitecture architecture = WinPeArchitecture.X64,
        string? adkRootPath = null)
    {
        _logger.LogInformation("Resolving available WinPE languages for Architecture={Architecture}.", architecture);
        if (!Enum.IsDefined(architecture))
        {
            _logger.LogWarning("Invalid architecture value for WinPE languages resolution: {Architecture}", architecture);
            return WinPeResult<IReadOnlyList<string>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                GetString("WinPe.ErrorInvalidArchitecture"));
        }

        WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(adkRootPath);
        if (!toolsResult.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve ADK tools while resolving WinPE languages: {ErrorCode}", toolsResult.Error?.Code);
            return WinPeResult<IReadOnlyList<string>>.Failure(toolsResult.Error!);
        }

        string ocRoot = GetOptionalComponentsRootPath(toolsResult.Value!.KitsRootPath, architecture);
        if (!Directory.Exists(ocRoot))
        {
            _logger.LogWarning("WinPE optional components directory not found: {OptionalComponentsRoot}", ocRoot);
            return WinPeResult<IReadOnlyList<string>>.Failure(
                WinPeErrorCodes.ToolNotFound,
                GetString("WinPe.ErrorOptionalComponentsFolderMissing"),
                Format("Common.ExpectedPathFormat", ocRoot));
        }

        string[] locales = Directory.GetDirectories(ocRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => WinPeLanguageUtility.Normalize(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation("Resolved {LocaleCount} WinPE locales for Architecture={Architecture}.", locales.Length, architecture);
        return WinPeResult<IReadOnlyList<string>>.Success(locales);
    }

    public async Task<WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>> GetUsbCandidatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving USB disk candidates.");
        WinPeResult<WinPeToolPaths> tools = _toolResolver.ResolveTools();
        if (!tools.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve ADK tools while querying USB candidates: {ErrorCode}", tools.Error?.Code);
            return WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>>.Failure(tools.Error!);
        }

        string work = WinPeDefaults.GetUsbQueryWorkingDirectoryPath();
        Directory.CreateDirectory(work);
        WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result =
            await _usbMediaService.GetUsbCandidatesAsync(tools.Value!, work, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("USB candidate query failed: {ErrorCode} - {ErrorMessage}", result.Error?.Code, result.Error?.Message);
            return result;
        }

        _logger.LogInformation("Resolved {CandidateCount} USB disk candidates.", result.Value?.Count ?? 0);
        return result;
    }

    public async Task<WinPeResult> CreateIsoAsync(IsoOutputOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogInformation("ISO creation requested. OutputIsoPath={OutputIsoPath}, Architecture={Architecture}, SignatureMode={SignatureMode}",
            options.OutputIsoPath,
            options.Architecture,
            options.SignatureMode);
        WinPeDiagnostic? validation = ValidateIsoOptions(options);
        if (validation is not null)
        {
            _logger.LogWarning("ISO creation validation failed: {ErrorCode} - {ErrorMessage}", validation.Code, validation.Message);
            return WinPeResult.Failure(validation);
        }

        if (!_operationProgressService.TryStart(OperationKind.IsoCreate, GetString("Media.StatusPreparingIso"), 0))
        {
            _logger.LogWarning("ISO creation rejected because another operation is already in progress.");
            return WinPeResult.Failure(WinPeErrorCodes.OperationBusy, GetString("Operation.ErrorBusy"));
        }

        WinPeBuildArtifact? artifact = null;
        string? isoWorkspacePath = null;
        string? preparedIsoOutputPath = null;
        try
        {
            WinPeResult<WinPePreparedMediaWorkspace> preparedWorkspace = await PrepareWorkspaceAsync(
                options.AdkRootPath,
                options.StagingDirectoryPath,
                options.WorkingDirectoryPath,
                options.Architecture,
                options.SignatureMode,
                options.BootImageSource,
                options.DriverCatalogUri,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                options.WinPeLanguage,
                options.FoundryConnectConfigurationJson,
                options.FoundryConnectAssetFiles,
                options.ExpertDeployConfigurationJson,
                options.AutopilotProfiles,
                stage => stage switch
                {
                    WinPeWorkspacePreparationStage.ResolvingDrivers => (30, GetString("Media.StatusResolvingDrivers")),
                    WinPeWorkspacePreparationStage.CustomizingImage => (48, GetString("Media.StatusCustomizingImage")),
                    WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => (66, GetString("Media.StatusApplyingSignaturePolicy")),
                    _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
                },
                cancellationToken).ConfigureAwait(false);
            if (!preparedWorkspace.IsSuccess)
            {
                return FailWithProgress(preparedWorkspace.Error!);
            }

            artifact = preparedWorkspace.Value!.Artifact;
            EnsureIsoOutputDirectoryExists(options.OutputIsoPath);
            isoWorkspacePath = PrepareIsoWorkspacePath(artifact.WorkingDirectoryPath);
            preparedIsoOutputPath = PrepareIsoOutputPath(options.OutputIsoPath);

            _operationProgressService.Report(82, GetString("Media.StatusCreatingIso"));
            if (options.ForceOverwriteOutput && File.Exists(preparedIsoOutputPath))
            {
                _logger.LogInformation("Deleting existing ISO before overwrite. OutputIsoPath={OutputIsoPath}", preparedIsoOutputPath);
                File.Delete(preparedIsoOutputPath);
            }

            string args = $"/ISO /F {WinPeProcessRunner.Quote(isoWorkspacePath)} {WinPeProcessRunner.Quote(preparedIsoOutputPath)}{(preparedWorkspace.Value.UseBootEx ? " /bootex" : string.Empty)}";
            _logger.LogInformation(
                "Creating ISO media from prepared workspace. SourceWorkspacePath={SourceWorkspacePath}, IsoWorkspacePath={IsoWorkspacePath}, PreparedIsoOutputPath={PreparedIsoOutputPath}, RequestedOutputIsoPath={RequestedOutputIsoPath}, UseBootEx={UseBootEx}",
                artifact.WorkingDirectoryPath,
                isoWorkspacePath,
                preparedIsoOutputPath,
                options.OutputIsoPath,
                preparedWorkspace.Value.UseBootEx);
            WinPeProcessExecution makeIso = await _processRunner.RunCmdScriptAsync(
                preparedWorkspace.Value.Tools.MakeWinPeMediaPath,
                args,
                isoWorkspacePath,
                cancellationToken).ConfigureAwait(false);
            if (!makeIso.IsSuccess || !File.Exists(preparedIsoOutputPath))
            {
                return FailWithProgress(new WinPeDiagnostic(
                    WinPeErrorCodes.IsoCreateFailed,
                    GetString("Media.ErrorIsoCreateFailed"),
                    makeIso.ToDiagnosticText()));
            }

            FinalizeIsoOutput(preparedIsoOutputPath, options.OutputIsoPath);

            _logger.LogInformation("ISO media file created successfully. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
            _operationProgressService.Complete(GetString("Media.StatusIsoCompleted"));
            _logger.LogInformation("ISO creation completed successfully. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected ISO creation failure for OutputIsoPath={OutputIsoPath}.", options.OutputIsoPath);
            return FailWithProgress(new WinPeDiagnostic(
                WinPeErrorCodes.InternalError,
                GetString("Media.ErrorUnexpectedIsoCreateFailure"),
                ex.ToString()));
        }
        finally
        {
            CleanupPreparedIsoOutput(options.OutputIsoPath, preparedIsoOutputPath);
            CleanupIsoWorkspace(artifact?.WorkingDirectoryPath, isoWorkspacePath);
            CleanupWorkspace(artifact, options.PreserveBuildWorkspace, "ISO");
        }
    }

    public async Task<WinPeResult> CreateUsbAsync(UsbOutputOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogInformation("USB creation requested. TargetDiskNumber={TargetDiskNumber}, Architecture={Architecture}, SignatureMode={SignatureMode}",
            options.TargetDiskNumber,
            options.Architecture,
            options.SignatureMode);
        WinPeDiagnostic? validation = ValidateUsbOptions(options);
        if (validation is not null)
        {
            _logger.LogWarning("USB creation validation failed: {ErrorCode} - {ErrorMessage}", validation.Code, validation.Message);
            return WinPeResult.Failure(validation);
        }

        if (!_operationProgressService.TryStart(OperationKind.UsbCreate, GetString("Media.StatusPreparingUsb"), 0))
        {
            _logger.LogWarning("USB creation rejected because another operation is already in progress.");
            return WinPeResult.Failure(WinPeErrorCodes.OperationBusy, GetString("Operation.ErrorBusy"));
        }

        WinPeBuildArtifact? artifact = null;
        try
        {
            WinPeResult<WinPePreparedMediaWorkspace> preparedWorkspace = await PrepareWorkspaceAsync(
                options.AdkRootPath,
                options.StagingDirectoryPath,
                options.WorkingDirectoryPath,
                options.Architecture,
                options.SignatureMode,
                options.BootImageSource,
                options.DriverCatalogUri,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                options.WinPeLanguage,
                options.FoundryConnectConfigurationJson,
                options.FoundryConnectAssetFiles,
                options.ExpertDeployConfigurationJson,
                options.AutopilotProfiles,
                stage => stage switch
                {
                    WinPeWorkspacePreparationStage.ResolvingDrivers => (30, GetString("Media.StatusResolvingDrivers")),
                    WinPeWorkspacePreparationStage.CustomizingImage => (48, GetString("Media.StatusCustomizingImage")),
                    WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => (62, GetString("Media.StatusApplyingSignaturePolicy")),
                    _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
                },
                cancellationToken).ConfigureAwait(false);
            if (!preparedWorkspace.IsSuccess)
            {
                return FailWithProgress(preparedWorkspace.Error!);
            }

            artifact = preparedWorkspace.Value!.Artifact;

            _operationProgressService.Report(80, GetString("Media.StatusProvisioningUsb"));
            _logger.LogInformation(
                "Starting USB provisioning and media population. WorkingDirectoryPath={WorkingDirectoryPath}, TargetDiskNumber={TargetDiskNumber}, UseBootEx={UseBootEx}",
                artifact.WorkingDirectoryPath,
                options.TargetDiskNumber,
                preparedWorkspace.Value.UseBootEx);
            WinPeResult<WinPeUsbProvisionResult> usb = await _usbMediaService.ProvisionAndPopulateAsync(
                options,
                artifact,
                preparedWorkspace.Value.Tools,
                preparedWorkspace.Value.UseBootEx,
                cancellationToken).ConfigureAwait(false);
            if (!usb.IsSuccess)
            {
                return FailWithProgress(usb.Error!);
            }

            _logger.LogInformation(
                "USB provisioning and media population completed. TargetDiskNumber={TargetDiskNumber}, BootDrive={BootDrive}, CacheDrive={CacheDrive}",
                options.TargetDiskNumber,
                usb.Value?.BootDriveLetter,
                usb.Value?.CacheDriveLetter);
            _operationProgressService.Complete(GetString("Media.StatusUsbCompleted"));
            _logger.LogInformation("USB creation completed successfully. TargetDiskNumber={TargetDiskNumber}", options.TargetDiskNumber);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected USB creation failure. TargetDiskNumber={TargetDiskNumber}", options.TargetDiskNumber);
            return FailWithProgress(new WinPeDiagnostic(
                WinPeErrorCodes.InternalError,
                GetString("Media.ErrorUnexpectedUsbCreateFailure"),
                ex.ToString()));
        }
        finally
        {
            CleanupWorkspace(artifact, options.PreserveBuildWorkspace, "USB");
        }
    }

    private async Task<WinPeResult<WinPePreparedMediaWorkspace>> PrepareWorkspaceAsync(
        string? adkRootPath,
        string stagingDirectoryPath,
        string? workingDirectoryPath,
        WinPeArchitecture architecture,
        WinPeSignatureMode signatureMode,
        WinPeBootImageSource bootImageSource,
        string driverCatalogUri,
        IReadOnlyList<WinPeVendorSelection> driverVendors,
        string? customDriverDirectoryPath,
        string winPeLanguage,
        string? foundryConnectConfigurationJson,
        IReadOnlyList<FoundryConnectProvisionedAssetFile> foundryConnectAssetFiles,
        string? expertDeployConfigurationJson,
        IReadOnlyList<Foundry.Models.Configuration.AutopilotProfileSettings> autopilotProfiles,
        Func<WinPeWorkspacePreparationStage, (int Progress, string Message)> progressMap,
        CancellationToken cancellationToken)
    {
        _operationProgressService.Report(8, GetString("Media.StatusResolvingAdk"));
        WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(adkRootPath);
        if (!toolsResult.IsSuccess)
        {
            return WinPeResult<WinPePreparedMediaWorkspace>.Failure(toolsResult.Error!);
        }

        WinPeToolPaths tools = toolsResult.Value!;
        _logger.LogInformation(
            "Resolved ADK tooling. DismPath={DismPath}, MakeWinPeMediaPath={MakeWinPeMediaPath}",
            tools.DismPath,
            tools.MakeWinPeMediaPath);

        _operationProgressService.Report(16, GetString("Media.StatusCreatingWorkspace"));
        WinPeResult<WinPeBuildArtifact> buildResult = await _buildService.BuildAsync(new WinPeBuildOptions
        {
            AdkRootPath = adkRootPath,
            OutputDirectoryPath = stagingDirectoryPath,
            WorkingDirectoryPath = workingDirectoryPath,
            Architecture = architecture,
            SignatureMode = signatureMode
        }, cancellationToken).ConfigureAwait(false);
        if (!buildResult.IsSuccess)
        {
            return WinPeResult<WinPePreparedMediaWorkspace>.Failure(buildResult.Error!);
        }

        WinPeBuildArtifact artifact = buildResult.Value!;
        (int customizationStartProgress, _) = progressMap(WinPeWorkspacePreparationStage.CustomizingImage);
        (int signaturePolicyStartProgress, _) = progressMap(WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy);
        int customizationProgressRange = Math.Max(0, signaturePolicyStartProgress - customizationStartProgress - 1);
        var progress = new Progress<WinPeWorkspacePreparationStage>(stage =>
        {
            (int progressValue, string message) = progressMap(stage);
            _operationProgressService.Report(progressValue, message);
        });
        var customizationProgress = new Progress<WinPeMountedImageCustomizationProgress>(update =>
        {
            int normalizedPercent = Math.Clamp(update.Percent, 0, 100);
            int absoluteProgress = customizationStartProgress;
            if (customizationProgressRange > 0)
            {
                absoluteProgress += (int)Math.Round(
                    customizationProgressRange * (normalizedPercent / 100d),
                    MidpointRounding.AwayFromZero);
            }

            _operationProgressService.Report(absoluteProgress, update.Status);
        });

        WinPeResult<WinPeWorkspacePreparationResult> preparation = await _workspacePreparationService.PrepareAsync(
            new WinPeWorkspacePreparationRequest
            {
                Artifact = artifact,
                Tools = tools,
                DriverCatalogUri = driverCatalogUri,
                DriverVendors = driverVendors,
                CustomDriverDirectoryPath = customDriverDirectoryPath,
                SignatureMode = signatureMode,
                BootImageSource = bootImageSource,
                WinPeLanguage = winPeLanguage,
                CustomizationProgress = customizationProgress,
                FoundryConnectConfigurationJson = foundryConnectConfigurationJson,
                FoundryConnectAssetFiles = foundryConnectAssetFiles,
                ExpertDeployConfigurationJson = expertDeployConfigurationJson,
                AutopilotProfiles = autopilotProfiles
            },
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!preparation.IsSuccess)
        {
            return WinPeResult<WinPePreparedMediaWorkspace>.Failure(preparation.Error!);
        }

        return WinPeResult<WinPePreparedMediaWorkspace>.Success(new WinPePreparedMediaWorkspace
        {
            Artifact = artifact,
            Tools = tools,
            UseBootEx = preparation.Value!.UseBootEx
        });
    }

    private WinPeResult FailWithProgress(WinPeDiagnostic diagnostic)
    {
        _operationProgressService.Fail(diagnostic.Message);

        if (string.Equals(diagnostic.Code, WinPeErrorCodes.InternalError, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("WinPE operation failed. Code={ErrorCode}, Message={ErrorMessage}, Details={ErrorDetails}",
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Details);
        }
        else
        {
            _logger.LogWarning("WinPE operation failed. Code={ErrorCode}, Message={ErrorMessage}, Details={ErrorDetails}",
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Details);
        }

        return WinPeResult.Failure(diagnostic);
    }

    private void CleanupWorkspace(WinPeBuildArtifact? artifact, bool preserveBuildWorkspace, string mediaKind)
    {
        if (artifact is not null && !preserveBuildWorkspace)
        {
            TryDeleteDirectory(artifact.WorkingDirectoryPath);
            _logger.LogDebug("{MediaKind} working directory cleanup completed. WorkingDirectoryPath={WorkingDirectoryPath}", mediaKind, artifact.WorkingDirectoryPath);
        }
        else if (artifact is not null)
        {
            _logger.LogInformation("{MediaKind} working directory preserved. WorkingDirectoryPath={WorkingDirectoryPath}", mediaKind, artifact.WorkingDirectoryPath);
        }
    }

    private void CleanupIsoWorkspace(string? sourceWorkspacePath, string? isoWorkspacePath)
    {
        if (string.IsNullOrWhiteSpace(sourceWorkspacePath) ||
            string.IsNullOrWhiteSpace(isoWorkspacePath) ||
            string.Equals(sourceWorkspacePath, isoWorkspacePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryDeleteDirectory(isoWorkspacePath);
        _logger.LogDebug("ASCII-safe ISO workspace cleanup completed. WorkingDirectoryPath={WorkingDirectoryPath}", isoWorkspacePath);
    }

    private void CleanupPreparedIsoOutput(string requestedIsoOutputPath, string? preparedIsoOutputPath)
    {
        if (string.IsNullOrWhiteSpace(preparedIsoOutputPath) ||
            string.Equals(requestedIsoOutputPath, preparedIsoOutputPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(preparedIsoOutputPath))
        {
            return;
        }

        TryDeleteFile(preparedIsoOutputPath);
        _logger.LogDebug("ASCII-safe prepared ISO output cleanup completed. OutputIsoPath={OutputIsoPath}", preparedIsoOutputPath);
    }

    private WinPeDiagnostic? ValidateIsoOptions(IsoOutputOptions? options)
    {
        if (options is null) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorIsoOptionsRequired"));
        if (string.IsNullOrWhiteSpace(options.StagingDirectoryPath) || !Directory.Exists(options.StagingDirectoryPath)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorStagingDirectoryMissing"), options.StagingDirectoryPath);
        if (string.IsNullOrWhiteSpace(options.OutputIsoPath) || !options.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorIsoOutputPathInvalid"), options.OutputIsoPath);
        if (!Enum.IsDefined(options.Architecture) || !Enum.IsDefined(options.SignatureMode)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorInvalidArchitectureOrSignature"));
        if (string.IsNullOrWhiteSpace(options.WinPeLanguage)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorWinPeLanguageRequired"));
        if (!string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath))
        {
            string customDirectory = options.CustomDriverDirectoryPath.Trim();
            if (!Directory.Exists(customDirectory)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorCustomDriverDirectoryMissing"), customDirectory);
            if (!Directory.EnumerateFiles(customDirectory, "*.inf", SearchOption.AllDirectories).Any()) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorCustomDriverDirectoryEmpty"), customDirectory);
        }
        return null;
    }

    private WinPeDiagnostic? ValidateUsbOptions(UsbOutputOptions? options)
    {
        if (options is null) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorUsbOptionsRequired"));
        if (string.IsNullOrWhiteSpace(options.StagingDirectoryPath) || !Directory.Exists(options.StagingDirectoryPath)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorStagingDirectoryMissing"), options.StagingDirectoryPath);
        if (!options.TargetDiskNumber.HasValue) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorTargetDiskRequired"));
        if (!Enum.IsDefined(options.Architecture) || !Enum.IsDefined(options.SignatureMode) || !Enum.IsDefined(options.PartitionStyle) || !Enum.IsDefined(options.FormatMode)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorUsbInvalidEnums"));
        if (string.IsNullOrWhiteSpace(options.WinPeLanguage)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorWinPeLanguageRequired"));
        if (options.Architecture == WinPeArchitecture.Arm64 && options.PartitionStyle == UsbPartitionStyle.Mbr) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorArm64RequiresGpt"));
        if (!string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath))
        {
            string customDirectory = options.CustomDriverDirectoryPath.Trim();
            if (!Directory.Exists(customDirectory)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorCustomDriverDirectoryMissing"), customDirectory);
            if (!Directory.EnumerateFiles(customDirectory, "*.inf", SearchOption.AllDirectories).Any()) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, GetString("Media.ErrorCustomDriverDirectoryEmpty"), customDirectory);
        }
        return null;
    }

    private string GetString(string key)
    {
        return _localizationService.Strings[key];
    }

    private string Format(string key, params object[] args)
    {
        return string.Format(_localizationService.CurrentCulture, GetString(key), args);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void EnsureIsoOutputDirectoryExists(string outputIsoPath)
    {
        string? outputDirectoryPath = Path.GetDirectoryName(outputIsoPath);
        if (string.IsNullOrWhiteSpace(outputDirectoryPath))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectoryPath);
    }

    private string PrepareIsoOutputPath(string requestedOutputIsoPath)
    {
        if (!ContainsNonAscii(requestedOutputIsoPath))
        {
            return requestedOutputIsoPath;
        }

        string isoOutputRoot = WinPeDefaults.GetIsoOutputTempRootPath();
        Directory.CreateDirectory(isoOutputRoot);

        string fileName = Path.GetFileName(requestedOutputIsoPath);
        string safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? $"foundry-winpe-{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.iso"
            : WinPeFileSystemHelper.SanitizePathSegment(fileName);
        if (!safeFileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName += ".iso";
        }

        string preparedIsoOutputPath = Path.Combine(isoOutputRoot, safeFileName);
        _logger.LogInformation(
            "Redirecting ISO creation to ASCII-safe output path. RequestedOutputIsoPath={RequestedOutputIsoPath}, PreparedIsoOutputPath={PreparedIsoOutputPath}",
            requestedOutputIsoPath,
            preparedIsoOutputPath);

        return preparedIsoOutputPath;
    }

    private void FinalizeIsoOutput(string preparedIsoOutputPath, string requestedOutputIsoPath)
    {
        if (string.Equals(preparedIsoOutputPath, requestedOutputIsoPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? requestedOutputDirectoryPath = Path.GetDirectoryName(requestedOutputIsoPath);
        if (!string.IsNullOrWhiteSpace(requestedOutputDirectoryPath))
        {
            Directory.CreateDirectory(requestedOutputDirectoryPath);
        }

        File.Copy(preparedIsoOutputPath, requestedOutputIsoPath, overwrite: true);
        _logger.LogInformation(
            "Copied prepared ISO from ASCII-safe output path to requested destination. PreparedIsoOutputPath={PreparedIsoOutputPath}, RequestedOutputIsoPath={RequestedOutputIsoPath}",
            preparedIsoOutputPath,
            requestedOutputIsoPath);
    }

    private string PrepareIsoWorkspacePath(string sourceWorkspacePath)
    {
        if (!ContainsNonAscii(sourceWorkspacePath))
        {
            return sourceWorkspacePath;
        }

        string isoWorkspaceRoot = WinPeDefaults.GetIsoWorkspaceRootPath();
        string directoryName = $"{WinPeFileSystemHelper.SanitizePathSegment(Path.GetFileName(sourceWorkspacePath))}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
        string isoWorkspacePath = Path.Combine(isoWorkspaceRoot, directoryName);

        _logger.LogInformation(
            "Mirroring WinPE workspace to ASCII-safe path for ISO creation. SourceWorkspacePath={SourceWorkspacePath}, IsoWorkspacePath={IsoWorkspacePath}",
            sourceWorkspacePath,
            isoWorkspacePath);

        CopyDirectory(sourceWorkspacePath, isoWorkspacePath);
        return isoWorkspacePath;
    }

    private static bool ContainsNonAscii(string value)
    {
        return value.Any(character => character > 127);
    }

    private static void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectoryPath, relativePath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
            string destinationFilePath = Path.Combine(destinationDirectoryPath, relativePath);
            string? destinationParentPath = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationParentPath))
            {
                Directory.CreateDirectory(destinationParentPath);
            }

            File.Copy(filePath, destinationFilePath, overwrite: true);
        }
    }

    private static string GetOptionalComponentsRootPath(string kitsRootPath, WinPeArchitecture architecture)
    {
        return Path.Combine(
            kitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            architecture.ToCopypeArchitecture(),
            "WinPE_OCs");
    }

}

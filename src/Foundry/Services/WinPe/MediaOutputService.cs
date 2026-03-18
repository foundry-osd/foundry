using Foundry.Services.Operations;
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
    private readonly ILogger<MediaOutputService> _logger;

    public MediaOutputService(
        IOperationProgressService operationProgressService,
        IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService workspacePreparationService,
        WinPeToolResolver toolResolver,
        WinPeProcessRunner processRunner,
        WinPeUsbMediaService usbMediaService,
        ILogger<MediaOutputService> logger)
    {
        _operationProgressService = operationProgressService;
        _buildService = buildService;
        _workspacePreparationService = workspacePreparationService;
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _usbMediaService = usbMediaService;
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
                "Architecture is invalid.");
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
                "WinPE optional components folder was not found.",
                $"Expected path: '{ocRoot}'.");
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

        string work = Path.Combine(Path.GetTempPath(), "Foundry", "UsbQuery");
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

        if (!_operationProgressService.TryStart(OperationKind.IsoCreate, "Preparing ISO creation.", 0))
        {
            _logger.LogWarning("ISO creation rejected because another operation is already in progress.");
            return WinPeResult.Failure(WinPeErrorCodes.OperationBusy, "Another operation is already in progress.");
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
                options.DriverCatalogUri,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                options.WinPeLanguage,
                options.ExpertDeployConfigurationJson,
                stage => stage switch
                {
                    WinPeWorkspacePreparationStage.ResolvingDrivers => (30, "Resolving and preparing drivers."),
                    WinPeWorkspacePreparationStage.CustomizingImage => (48, "Applying image customizations."),
                    WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => (66, "Applying signature policy."),
                    _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
                },
                cancellationToken).ConfigureAwait(false);
            if (!preparedWorkspace.IsSuccess)
            {
                return FailWithProgress(preparedWorkspace.Error!);
            }

            artifact = preparedWorkspace.Value!.Artifact;

            _operationProgressService.Report(82, "Creating ISO media.");
            if (options.ForceOverwriteOutput && File.Exists(options.OutputIsoPath))
            {
                _logger.LogInformation("Deleting existing ISO before overwrite. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
                File.Delete(options.OutputIsoPath);
            }

            string args = $"/ISO /F {WinPeProcessRunner.Quote(artifact.WorkingDirectoryPath)} {WinPeProcessRunner.Quote(options.OutputIsoPath)}{(preparedWorkspace.Value.UseBootEx ? " /bootex" : string.Empty)}";
            _logger.LogInformation(
                "Creating ISO media from prepared workspace. WorkingDirectoryPath={WorkingDirectoryPath}, OutputIsoPath={OutputIsoPath}, UseBootEx={UseBootEx}",
                artifact.WorkingDirectoryPath,
                options.OutputIsoPath,
                preparedWorkspace.Value.UseBootEx);
            WinPeProcessExecution makeIso = await _processRunner.RunCmdScriptAsync(
                preparedWorkspace.Value.Tools.MakeWinPeMediaPath,
                args,
                artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            if (!makeIso.IsSuccess || !File.Exists(options.OutputIsoPath))
            {
                return FailWithProgress(new WinPeDiagnostic(WinPeErrorCodes.IsoCreateFailed, "Failed to create ISO media.", makeIso.ToDiagnosticText()));
            }

            _logger.LogInformation("ISO media file created successfully. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
            _operationProgressService.Complete("ISO creation completed.");
            _logger.LogInformation("ISO creation completed successfully. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected ISO creation failure for OutputIsoPath={OutputIsoPath}.", options.OutputIsoPath);
            return FailWithProgress(new WinPeDiagnostic(WinPeErrorCodes.InternalError, "Unexpected ISO creation failure.", ex.ToString()));
        }
        finally
        {
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

        if (!_operationProgressService.TryStart(OperationKind.UsbCreate, "Preparing USB creation.", 0))
        {
            _logger.LogWarning("USB creation rejected because another operation is already in progress.");
            return WinPeResult.Failure(WinPeErrorCodes.OperationBusy, "Another operation is already in progress.");
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
                options.DriverCatalogUri,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                options.WinPeLanguage,
                options.ExpertDeployConfigurationJson,
                stage => stage switch
                {
                    WinPeWorkspacePreparationStage.ResolvingDrivers => (30, "Resolving and preparing drivers."),
                    WinPeWorkspacePreparationStage.CustomizingImage => (48, "Applying image customizations."),
                    WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => (62, "Applying signature policy."),
                    _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
                },
                cancellationToken).ConfigureAwait(false);
            if (!preparedWorkspace.IsSuccess)
            {
                return FailWithProgress(preparedWorkspace.Error!);
            }

            artifact = preparedWorkspace.Value!.Artifact;

            _operationProgressService.Report(80, "Provisioning and populating USB.");
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
            _operationProgressService.Complete("USB creation completed.");
            _logger.LogInformation("USB creation completed successfully. TargetDiskNumber={TargetDiskNumber}", options.TargetDiskNumber);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected USB creation failure. TargetDiskNumber={TargetDiskNumber}", options.TargetDiskNumber);
            return FailWithProgress(new WinPeDiagnostic(WinPeErrorCodes.InternalError, "Unexpected USB creation failure.", ex.ToString()));
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
        string driverCatalogUri,
        IReadOnlyList<WinPeVendorSelection> driverVendors,
        string? customDriverDirectoryPath,
        string winPeLanguage,
        string? expertDeployConfigurationJson,
        Func<WinPeWorkspacePreparationStage, (int Progress, string Message)> progressMap,
        CancellationToken cancellationToken)
    {
        _operationProgressService.Report(8, "Resolving ADK tooling.");
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

        _operationProgressService.Report(16, "Creating WinPE workspace.");
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
        var progress = new Progress<WinPeWorkspacePreparationStage>(stage =>
        {
            (int progressValue, string message) = progressMap(stage);
            _operationProgressService.Report(progressValue, message);
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
                WinPeLanguage = winPeLanguage,
                ExpertDeployConfigurationJson = expertDeployConfigurationJson
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

    private static WinPeDiagnostic? ValidateIsoOptions(IsoOutputOptions? options)
    {
        if (options is null) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "ISO options are required.");
        if (string.IsNullOrWhiteSpace(options.StagingDirectoryPath) || !Directory.Exists(options.StagingDirectoryPath)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Staging directory does not exist.", options.StagingDirectoryPath);
        if (string.IsNullOrWhiteSpace(options.OutputIsoPath) || !options.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Output ISO path must end with .iso.", options.OutputIsoPath);
        if (!Enum.IsDefined(options.Architecture) || !Enum.IsDefined(options.SignatureMode)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Architecture or signature mode is invalid.");
        if (string.IsNullOrWhiteSpace(options.WinPeLanguage)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "WinPE language is required.");
        if (!string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath))
        {
            string customDirectory = options.CustomDriverDirectoryPath.Trim();
            if (!Directory.Exists(customDirectory)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Custom driver directory does not exist.", customDirectory);
            if (!Directory.EnumerateFiles(customDirectory, "*.inf", SearchOption.AllDirectories).Any()) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Custom driver directory does not contain any .inf files.", customDirectory);
        }
        return null;
    }

    private static WinPeDiagnostic? ValidateUsbOptions(UsbOutputOptions? options)
    {
        if (options is null) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "USB options are required.");
        if (string.IsNullOrWhiteSpace(options.StagingDirectoryPath) || !Directory.Exists(options.StagingDirectoryPath)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Staging directory does not exist.", options.StagingDirectoryPath);
        if (!options.TargetDiskNumber.HasValue) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "TargetDiskNumber is required.");
        if (!Enum.IsDefined(options.Architecture) || !Enum.IsDefined(options.SignatureMode) || !Enum.IsDefined(options.PartitionStyle) || !Enum.IsDefined(options.FormatMode)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "USB options contain invalid enum values.");
        if (string.IsNullOrWhiteSpace(options.WinPeLanguage)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "WinPE language is required.");
        if (options.Architecture == WinPeArchitecture.Arm64 && options.PartitionStyle == UsbPartitionStyle.Mbr) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "ARM64 only supports GPT partition style.");
        if (!string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath))
        {
            string customDirectory = options.CustomDriverDirectoryPath.Trim();
            if (!Directory.Exists(customDirectory)) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Custom driver directory does not exist.", customDirectory);
            if (!Directory.EnumerateFiles(customDirectory, "*.inf", SearchOption.AllDirectories).Any()) return new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "Custom driver directory does not contain any .inf files.", customDirectory);
        }
        return null;
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

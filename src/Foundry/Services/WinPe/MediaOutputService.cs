using System.Text;
using System.Globalization;
using System.IO.Compression;
using Foundry.Services.Operations;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

public sealed class MediaOutputService : IMediaOutputService
{
    private readonly IOperationProgressService _operationProgressService;
    private readonly IWinPeBuildService _buildService;
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly WinPeToolResolver _toolResolver = new();
    private readonly WinPeProcessRunner _processRunner = new();
    private readonly WinPeDriverPackageService _driverPackageService;
    private readonly WinPeUsbMediaService _usbMediaService;
    private readonly ILogger<MediaOutputService> _logger;

    public MediaOutputService(
        IOperationProgressService operationProgressService,
        IWinPeBuildService buildService,
        IWinPeDriverCatalogService driverCatalogService,
        IWinPeDriverInjectionService driverInjectionService,
        ILoggerFactory loggerFactory,
        ILogger<MediaOutputService> logger)
    {
        _operationProgressService = operationProgressService;
        _buildService = buildService;
        _driverCatalogService = driverCatalogService;
        _driverInjectionService = driverInjectionService;
        _logger = logger;
        _driverPackageService = new WinPeDriverPackageService(_processRunner, loggerFactory.CreateLogger<WinPeDriverPackageService>());
        _usbMediaService = new WinPeUsbMediaService(_processRunner, loggerFactory.CreateLogger<WinPeUsbMediaService>());
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
            .Select(name => NormalizeWinPeLanguageCode(name!))
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
            _operationProgressService.Report(8, "Resolving ADK tooling.");
            WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(options.AdkRootPath);
            if (!toolsResult.IsSuccess)
            {
                return FailWithProgress(toolsResult.Error!);
            }

            WinPeToolPaths tools = toolsResult.Value!;
            _logger.LogInformation(
                "Resolved ADK tooling for ISO creation. DismPath={DismPath}, MakeWinPeMediaPath={MakeWinPeMediaPath}",
                tools.DismPath,
                tools.MakeWinPeMediaPath);
            _operationProgressService.Report(16, "Creating WinPE workspace.");
            WinPeResult<WinPeBuildArtifact> buildResult = await _buildService.BuildAsync(new WinPeBuildOptions
            {
                AdkRootPath = options.AdkRootPath,
                OutputDirectoryPath = options.StagingDirectoryPath,
                WorkingDirectoryPath = options.WorkingDirectoryPath,
                Architecture = options.Architecture,
                SignatureMode = options.SignatureMode
            }, cancellationToken).ConfigureAwait(false);

            if (!buildResult.IsSuccess)
            {
                return FailWithProgress(buildResult.Error!);
            }

            artifact = buildResult.Value!;
            _operationProgressService.Report(30, "Resolving and preparing drivers.");
            WinPeResult<IReadOnlyList<string>> drivers = await ResolveDriversAsync(
                options.DriverCatalogUri,
                options.Architecture,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                artifact,
                tools,
                cancellationToken).ConfigureAwait(false);
            if (!drivers.IsSuccess)
            {
                return FailWithProgress(drivers.Error!);
            }

            _logger.LogInformation(
                "Resolved {DriverDirectoryCount} driver directory path(s) for ISO creation. WorkingDirectoryPath={WorkingDirectoryPath}",
                drivers.Value!.Count,
                artifact.WorkingDirectoryPath);
            _operationProgressService.Report(48, "Applying image customizations.");
            _logger.LogInformation(
                "Starting WinPE image customization for ISO creation. WorkingDirectoryPath={WorkingDirectoryPath}, DriverDirectoryCount={DriverDirectoryCount}, WinPeLanguage={WinPeLanguage}",
                artifact.WorkingDirectoryPath,
                drivers.Value!.Count,
                options.WinPeLanguage);
            WinPeResult customize = await CustomizeImageAsync(artifact, tools, drivers.Value!, options.WinPeLanguage, cancellationToken).ConfigureAwait(false);
            if (!customize.IsSuccess)
            {
                return FailWithProgress(customize.Error!);
            }

            _logger.LogInformation("WinPE image customization completed for ISO creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            _operationProgressService.Report(66, "Applying signature policy.");
            bool bootEx = false;
            if (options.SignatureMode == WinPeSignatureMode.Pca2023)
            {
                _logger.LogInformation("Evaluating PCA2023 signature policy for ISO creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
                bootEx = await _toolResolver.IsBootExSupportedAsync(tools, _processRunner, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("PCA2023 signature policy evaluated for ISO creation. BootExSupported={BootExSupported}", bootEx);
                if (!bootEx)
                {
                    WinPeResult remediation = await RunRemediationIfConfiguredAsync(options.RunPca2023RemediationWhenBootExUnsupported, options.Pca2023RemediationScriptPath, artifact, tools, cancellationToken).ConfigureAwait(false);
                    if (!remediation.IsSuccess)
                    {
                        return FailWithProgress(remediation.Error!);
                    }

                    _logger.LogInformation("PCA2023 remediation fallback completed for ISO creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
                }
            }

            _operationProgressService.Report(82, "Creating ISO media.");
            if (options.ForceOverwriteOutput && File.Exists(options.OutputIsoPath))
            {
                _logger.LogInformation("Deleting existing ISO before overwrite. OutputIsoPath={OutputIsoPath}", options.OutputIsoPath);
                File.Delete(options.OutputIsoPath);
            }

            string args = $"/ISO /F {WinPeProcessRunner.Quote(artifact.WorkingDirectoryPath)} {WinPeProcessRunner.Quote(options.OutputIsoPath)}{(bootEx ? " /bootex" : string.Empty)}";
            _logger.LogInformation(
                "Creating ISO media from prepared workspace. WorkingDirectoryPath={WorkingDirectoryPath}, OutputIsoPath={OutputIsoPath}, UseBootEx={UseBootEx}",
                artifact.WorkingDirectoryPath,
                options.OutputIsoPath,
                bootEx);
            WinPeProcessExecution makeIso = await _processRunner.RunCmdScriptAsync(tools.MakeWinPeMediaPath, args, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
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
            if (artifact is not null && !options.PreserveBuildWorkspace)
            {
                TryDeleteDirectory(artifact.WorkingDirectoryPath);
                _logger.LogDebug("ISO working directory cleanup completed. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            }
            else if (artifact is not null)
            {
                _logger.LogInformation("ISO working directory preserved. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            }
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
            _operationProgressService.Report(8, "Resolving ADK tooling.");
            WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(options.AdkRootPath);
            if (!toolsResult.IsSuccess)
            {
                return FailWithProgress(toolsResult.Error!);
            }

            WinPeToolPaths tools = toolsResult.Value!;
            _logger.LogInformation(
                "Resolved ADK tooling for USB creation. DismPath={DismPath}, MakeWinPeMediaPath={MakeWinPeMediaPath}",
                tools.DismPath,
                tools.MakeWinPeMediaPath);
            _operationProgressService.Report(16, "Creating WinPE workspace.");
            WinPeResult<WinPeBuildArtifact> buildResult = await _buildService.BuildAsync(new WinPeBuildOptions
            {
                AdkRootPath = options.AdkRootPath,
                OutputDirectoryPath = options.StagingDirectoryPath,
                WorkingDirectoryPath = options.WorkingDirectoryPath,
                Architecture = options.Architecture,
                SignatureMode = options.SignatureMode
            }, cancellationToken).ConfigureAwait(false);

            if (!buildResult.IsSuccess)
            {
                return FailWithProgress(buildResult.Error!);
            }

            artifact = buildResult.Value!;
            _operationProgressService.Report(30, "Resolving and preparing drivers.");
            WinPeResult<IReadOnlyList<string>> drivers = await ResolveDriversAsync(
                options.DriverCatalogUri,
                options.Architecture,
                options.DriverVendors,
                options.CustomDriverDirectoryPath,
                artifact,
                tools,
                cancellationToken).ConfigureAwait(false);
            if (!drivers.IsSuccess)
            {
                return FailWithProgress(drivers.Error!);
            }

            _logger.LogInformation(
                "Resolved {DriverDirectoryCount} driver directory path(s) for USB creation. WorkingDirectoryPath={WorkingDirectoryPath}",
                drivers.Value!.Count,
                artifact.WorkingDirectoryPath);
            _operationProgressService.Report(48, "Applying image customizations.");
            _logger.LogInformation(
                "Starting WinPE image customization for USB creation. WorkingDirectoryPath={WorkingDirectoryPath}, DriverDirectoryCount={DriverDirectoryCount}, WinPeLanguage={WinPeLanguage}",
                artifact.WorkingDirectoryPath,
                drivers.Value!.Count,
                options.WinPeLanguage);
            WinPeResult customize = await CustomizeImageAsync(artifact, tools, drivers.Value!, options.WinPeLanguage, cancellationToken).ConfigureAwait(false);
            if (!customize.IsSuccess)
            {
                return FailWithProgress(customize.Error!);
            }

            _logger.LogInformation("WinPE image customization completed for USB creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            _operationProgressService.Report(62, "Applying signature policy.");
            bool bootEx = false;
            if (options.SignatureMode == WinPeSignatureMode.Pca2023)
            {
                _logger.LogInformation("Evaluating PCA2023 signature policy for USB creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
                bootEx = await _toolResolver.IsBootExSupportedAsync(tools, _processRunner, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("PCA2023 signature policy evaluated for USB creation. BootExSupported={BootExSupported}", bootEx);
                if (!bootEx)
                {
                    WinPeResult remediation = await RunRemediationIfConfiguredAsync(options.RunPca2023RemediationWhenBootExUnsupported, options.Pca2023RemediationScriptPath, artifact, tools, cancellationToken).ConfigureAwait(false);
                    if (!remediation.IsSuccess)
                    {
                        return FailWithProgress(remediation.Error!);
                    }

                    _logger.LogInformation("PCA2023 remediation fallback completed for USB creation. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
                }
            }

            _operationProgressService.Report(80, "Provisioning and populating USB.");
            _logger.LogInformation(
                "Starting USB provisioning and media population. WorkingDirectoryPath={WorkingDirectoryPath}, TargetDiskNumber={TargetDiskNumber}, UseBootEx={UseBootEx}",
                artifact.WorkingDirectoryPath,
                options.TargetDiskNumber,
                bootEx);
            WinPeResult<WinPeUsbProvisionResult> usb = await _usbMediaService.ProvisionAndPopulateAsync(options, artifact, tools, bootEx, cancellationToken).ConfigureAwait(false);
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
            if (artifact is not null && !options.PreserveBuildWorkspace)
            {
                TryDeleteDirectory(artifact.WorkingDirectoryPath);
                _logger.LogDebug("USB working directory cleanup completed. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            }
            else if (artifact is not null)
            {
                _logger.LogInformation("USB working directory preserved. WorkingDirectoryPath={WorkingDirectoryPath}", artifact.WorkingDirectoryPath);
            }
        }
    }

    private async Task<WinPeResult<IReadOnlyList<string>>> ResolveDriversAsync(
        string catalogUri,
        WinPeArchitecture architecture,
        IReadOnlyList<WinPeVendorSelection> driverVendors,
        string? customDriverDirectoryPath,
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        WinPeVendorSelection[] normalizedVendors = driverVendors
            .Where(vendor => vendor != WinPeVendorSelection.Any)
            .Distinct()
            .ToArray();

        string normalizedCustomDirectory = customDriverDirectoryPath?.Trim() ?? string.Empty;
        bool hasCustomDirectory = !string.IsNullOrWhiteSpace(normalizedCustomDirectory);

        if (normalizedVendors.Length == 0 && !hasCustomDirectory)
        {
            return WinPeResult<IReadOnlyList<string>>.Success(Array.Empty<string>());
        }

        if (hasCustomDirectory)
        {
            if (!Directory.Exists(normalizedCustomDirectory))
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not exist.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }

            bool hasInf = Directory.EnumerateFiles(normalizedCustomDirectory, "*.inf", SearchOption.AllDirectories)
                .Any();
            if (!hasInf)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not contain any .inf files.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }
        }

        var resolvedPaths = new List<string>();

        if (normalizedVendors.Length > 0)
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> catalog = await _driverCatalogService.GetCatalogAsync(new WinPeDriverCatalogOptions
            {
                CatalogUri = catalogUri,
                Architecture = architecture,
                Vendors = normalizedVendors
            }, cancellationToken).ConfigureAwait(false);

            if (!catalog.IsSuccess)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(catalog.Error!);
            }

            WinPeDriverCatalogEntry[] selectedPackages = catalog.Value?
                .GroupBy(item => item.Vendor)
                .Select(group => group
                    .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                    .First())
                .ToArray() ?? [];

            if (selectedPackages.Length > 0)
            {
                WinPeResult<WinPePreparedDriverSet> prepared = await _driverPackageService.PrepareAsync(
                    selectedPackages,
                    Path.Combine(artifact.DriverWorkspacePath, "downloads"),
                    Path.Combine(artifact.DriverWorkspacePath, "extracted"),
                    cancellationToken).ConfigureAwait(false);
                if (!prepared.IsSuccess)
                {
                    return WinPeResult<IReadOnlyList<string>>.Failure(prepared.Error!);
                }

                resolvedPaths.AddRange(prepared.Value!.ExtractionDirectories);
            }
        }

        if (hasCustomDirectory)
        {
            resolvedPaths.Add(normalizedCustomDirectory);
        }

        return WinPeResult<IReadOnlyList<string>>.Success(
            resolvedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private async Task<WinPeResult> CustomizeImageAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        IReadOnlyList<string> driverDirectories,
        string winPeLanguage,
        CancellationToken cancellationToken)
    {
        string normalizedLocale = NormalizeWinPeLanguageCode(winPeLanguage);
        if (!TryResolveInputLocale(normalizedLocale, out string canonicalLocale, out string inputLocale))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to resolve keyboard layout from selected WinPE language.",
                $"Selected language: '{canonicalLocale}'.");
        }

        _logger.LogInformation(
            "Mounting WinPE image for customization. BootWimPath={BootWimPath}, MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}",
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            normalizedLocale);
        WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(_processRunner, tools.DismPath, artifact.BootWimPath, artifact.MountDirectoryPath, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!mount.IsSuccess)
        {
            return WinPeResult.Failure(mount.Error!);
        }

        await using WinPeMountSession session = mount.Value!;
        _logger.LogInformation("Mounted WinPE image for customization. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        if (driverDirectories.Count > 0)
        {
            _logger.LogInformation(
                "Starting driver injection into mounted WinPE image. DriverDirectoryCount={DriverDirectoryCount}, MountDirectoryPath={MountDirectoryPath}",
                driverDirectories.Count,
                session.MountDirectoryPath);
            WinPeResult inject = await _driverInjectionService.InjectAsync(new WinPeDriverInjectionOptions
            {
                MountedImagePath = session.MountDirectoryPath,
                DriverPackagePaths = driverDirectories,
                RecurseSubdirectories = true,
                DismExecutablePath = tools.DismPath,
                WorkingDirectoryPath = artifact.WorkingDirectoryPath
            }, cancellationToken).ConfigureAwait(false);

            if (!inject.IsSuccess)
            {
                await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
                return inject;
            }

            _logger.LogInformation("Driver injection completed for mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }
        else
        {
            _logger.LogInformation("Skipping driver injection because no driver directories were resolved. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

        _logger.LogInformation("Adding required WinPE optional components. MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}", session.MountDirectoryPath, normalizedLocale);
        WinPeResult addComponentsResult = await AddRequiredOptionalComponentsAsync(
            session.MountDirectoryPath,
            artifact.Architecture,
            tools,
            normalizedLocale,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!addComponentsResult.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return addComponentsResult;
        }

        _logger.LogInformation("Required WinPE optional components added successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        _logger.LogInformation("Applying WinPE international settings. CanonicalLocale={CanonicalLocale}, InputLocale={InputLocale}", canonicalLocale, inputLocale);
        WinPeResult intlResult = await ApplyInternationalSettingsAsync(
            session.MountDirectoryPath,
            tools,
            canonicalLocale,
            inputLocale,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!intlResult.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return intlResult;
        }

        _logger.LogInformation("Applied WinPE international settings successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        string system32 = Path.Combine(session.MountDirectoryPath, "Windows", "System32");
        Directory.CreateDirectory(system32);
        string bootstrapScriptContent;
        try
        {
            bootstrapScriptContent = WinPeDefaults.GetDefaultBootstrapScriptContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load embedded WinPE bootstrap script content.");
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to load embedded WinPE bootstrap script.",
                ex.ToString());
        }

        await File.WriteAllTextAsync(
            Path.Combine(system32, WinPeDefaults.DefaultBootstrapScriptFileName),
            bootstrapScriptContent,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Wrote bootstrap script into mounted WinPE image. System32Path={System32Path}", system32);

        WinPeResult localDeployProvisioning = await ProvisionLocalDeployArchiveInImageAsync(
            session.MountDirectoryPath,
            artifact.Architecture,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!localDeployProvisioning.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return localDeployProvisioning;
        }

        _logger.LogInformation("Provisioned local Foundry.Deploy archive into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult sevenZipProvisioning = ProvisionBundledSevenZipInImage(
            session.MountDirectoryPath,
            artifact.Architecture);
        if (!sevenZipProvisioning.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return sevenZipProvisioning;
        }

        _logger.LogInformation("Provisioned bundled 7-Zip tools into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        string startnet = Path.Combine(session.MountDirectoryPath, WinPeDefaults.DefaultStartnetPathInImage);
        string[] lines = File.Exists(startnet) ? await File.ReadAllLinesAsync(startnet, cancellationToken).ConfigureAwait(false) : ["wpeinit"];
        var merged = lines.ToList();
        if (!merged.Any(line => line.Contains(WinPeDefaults.DefaultBootstrapScriptFileName, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(WinPeDefaults.DefaultBootstrapInvocation);
        }
        await File.WriteAllLinesAsync(startnet, merged, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated startnet.cmd in mounted WinPE image. StartnetPath={StartnetPath}", startnet);

        _logger.LogInformation("Committing mounted WinPE image changes. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult commit = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (commit.IsSuccess)
        {
            _logger.LogInformation("Committed mounted WinPE image changes successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

        return commit;
    }

    private async Task<WinPeResult> ApplyInternationalSettingsAsync(
        string mountedImagePath,
        WinPeToolPaths tools,
        string canonicalLocale,
        string inputLocale,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string[] dismCommands =
        [
            $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-AllIntl:{canonicalLocale}",
            $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-InputLocale:{inputLocale}"
        ];

        foreach (string args in dismCommands)
        {
            WinPeProcessExecution execution = await _processRunner.RunAsync(
                tools.DismPath,
                args,
                workingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to apply WinPE international settings.",
                    execution.ToDiagnosticText());
            }
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> AddRequiredOptionalComponentsAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        WinPeToolPaths tools,
        string winPeLanguage,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string ocRoot = GetOptionalComponentsRootPath(tools.KitsRootPath, architecture);

        if (!Directory.Exists(ocRoot))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "WinPE optional components folder was not found.",
                $"Expected path: '{ocRoot}'.");
        }

        // Keep installation order aligned with Microsoft dependency requirements:
        // WinPE-WMI > WinPE-NetFX > WinPE-Scripting > WinPE-PowerShell > (WinPE-DismCmdlets, WinPE-StorageWMI).
        // WinPE-WinReCfg is added explicitly so winrecfg.exe is available in the generated WinPE image.
        string[] components =
        [
            "WinPE-WMI",
            "WinPE-NetFX",
            "WinPE-Scripting",
            "WinPE-PowerShell",
            "WinPE-WinReCfg",
            "WinPE-DismCmdlets",
            "WinPE-StorageWMI",
            "WinPE-Dot3Svc",
            "WinPE-EnhancedStorage"
        ];
        string normalizedLocale = NormalizeWinPeLanguageCode(winPeLanguage);
        string languagePackCab = Path.Combine(ocRoot, normalizedLocale, "lp.cab");
        if (File.Exists(languagePackCab))
        {
            WinPeProcessExecution addLanguagePack = await _processRunner.RunAsync(
                tools.DismPath,
                $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(languagePackCab)}",
                workingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!addLanguagePack.IsSuccess)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to add WinPE language pack.",
                    addLanguagePack.ToDiagnosticText());
            }
        }
        else
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "The selected WinPE language pack was not found.",
                $"Expected package: '{languagePackCab}'.");
        }

        int installed = 0;
        foreach (string component in components)
        {
            string neutral = Path.Combine(ocRoot, $"{component}.cab");
            if (File.Exists(neutral))
            {
                WinPeProcessExecution addNeutral = await _processRunner.RunAsync(
                    tools.DismPath,
                    $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(neutral)}",
                    workingDirectoryPath,
                    cancellationToken).ConfigureAwait(false);

                if (!addNeutral.IsSuccess)
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.BuildFailed,
                        $"Failed to add optional component '{component}'.",
                        addNeutral.ToDiagnosticText());
                }

                installed++;
            }

            string localeCab = Path.Combine(ocRoot, normalizedLocale, $"{component}_{normalizedLocale}.cab");

            if (File.Exists(localeCab))
            {
                WinPeProcessExecution addLocale = await _processRunner.RunAsync(
                    tools.DismPath,
                    $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(localeCab)}",
                    workingDirectoryPath,
                    cancellationToken).ConfigureAwait(false);

                if (!addLocale.IsSuccess)
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.BuildFailed,
                        $"Failed to add localized optional component '{component}'.",
                        addLocale.ToDiagnosticText());
                }
            }
        }

        return installed > 0
            ? WinPeResult.Success()
            : WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Required WinPE optional components were not found in ADK.",
                $"No required component CAB was found under '{ocRoot}'.");
    }

    private async Task<WinPeResult> ProvisionLocalDeployArchiveInImageAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (!IsEnabledEnvironmentFlag(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable)))
        {
            return WinPeResult.Success();
        }

        _logger.LogInformation("Provisioning local Foundry.Deploy archive into mounted WinPE image. Architecture={Architecture}", architecture);
        WinPeResult<string> archiveResult = await ResolveLocalDeployArchivePathAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve local Foundry.Deploy archive path. Code={ErrorCode}, Message={ErrorMessage}",
                archiveResult.Error?.Code,
                archiveResult.Error?.Message);
            return WinPeResult.Failure(archiveResult.Error!);
        }

        string destinationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedDeployArchivePathInImage);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for local Foundry.Deploy archive.",
                $"Destination: '{destinationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(archiveResult.Value!, destinationPath, overwrite: true);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy local Foundry.Deploy archive into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to copy local Foundry.Deploy archive into mounted WinPE image.",
                ex.ToString());
        }
    }

    private WinPeResult ProvisionBundledSevenZipInImage(
        string mountedImagePath,
        WinPeArchitecture architecture)
    {
        string sourceRootPath = Path.Combine(AppContext.BaseDirectory, WinPeDefaults.BundledSevenZipRelativePath);
        if (!Directory.Exists(sourceRootPath))
        {
            _logger.LogWarning("Bundled 7-Zip assets folder not found. SourceRootPath={SourceRootPath}", sourceRootPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip assets were not found.",
                $"Expected path: '{sourceRootPath}'.");
        }

        string runtimeFolder = architecture.ToSevenZipRuntimeFolder();
        string sourceExecutablePath = Path.Combine(sourceRootPath, runtimeFolder, "7za.exe");
        if (!File.Exists(sourceExecutablePath))
        {
            _logger.LogWarning("Bundled 7-Zip executable not found for runtime folder {RuntimeFolder}. Path={ExecutablePath}", runtimeFolder, sourceExecutablePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip executable was not found for target architecture.",
                $"Expected file: '{sourceExecutablePath}'.");
        }

        string sourceLicensePath = Path.Combine(sourceRootPath, "License.txt");
        if (!File.Exists(sourceLicensePath))
        {
            _logger.LogWarning("Bundled 7-Zip license file not found. Path={LicensePath}", sourceLicensePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip license file was not found.",
                $"Expected file: '{sourceLicensePath}'.");
        }

        string sourceReadmePath = Path.Combine(sourceRootPath, "readme.txt");
        if (!File.Exists(sourceReadmePath))
        {
            _logger.LogWarning("Bundled 7-Zip readme file not found. Path={ReadmePath}", sourceReadmePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip readme file was not found.",
                $"Expected file: '{sourceReadmePath}'.");
        }

        string destinationExecutablePath = Path.Combine(
            mountedImagePath,
            WinPeDefaults.EmbeddedSevenZipToolsPathInImage,
            runtimeFolder,
            "7za.exe");
        string destinationToolsRootPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedSevenZipToolsPathInImage);

        string? destinationDirectoryPath = Path.GetDirectoryName(destinationExecutablePath);
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for bundled 7-Zip provisioning.",
                $"Destination file: '{destinationExecutablePath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            Directory.CreateDirectory(destinationToolsRootPath);
            File.Copy(sourceExecutablePath, destinationExecutablePath, overwrite: true);
            File.Copy(sourceLicensePath, Path.Combine(destinationToolsRootPath, "License.txt"), overwrite: true);
            File.Copy(sourceReadmePath, Path.Combine(destinationToolsRootPath, "readme.txt"), overwrite: true);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision bundled 7-Zip tools into mounted WinPE image. DestinationToolsRootPath={DestinationToolsRootPath}", destinationToolsRootPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision bundled 7-Zip executable into mounted WinPE image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult<string>> ResolveLocalDeployArchivePathAsync(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string configuredArchivePath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployArchiveEnvironmentVariable) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredArchivePath))
        {
            if (!File.Exists(configuredArchivePath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured local Foundry.Deploy archive was not found.",
                    $"Set {WinPeDefaults.LocalDeployArchiveEnvironmentVariable} to an existing .zip file. Path: '{configuredArchivePath}'.");
            }

            return WinPeResult<string>.Success(configuredArchivePath);
        }

        string configuredProjectPath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable) ?? string.Empty).Trim();
        string projectPath;
        if (!string.IsNullOrWhiteSpace(configuredProjectPath))
        {
            if (!File.Exists(configuredProjectPath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured Foundry.Deploy project file was not found.",
                    $"Set {WinPeDefaults.LocalDeployProjectEnvironmentVariable} to an existing .csproj file. Path: '{configuredProjectPath}'.");
            }

            projectPath = configuredProjectPath;
        }
        else if (!TryFindFoundryDeployProjectPath(out projectPath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to locate Foundry.Deploy project for local WinPE embedding.",
                $"Set {WinPeDefaults.LocalDeployArchiveEnvironmentVariable} to a .zip archive or {WinPeDefaults.LocalDeployProjectEnvironmentVariable} to Foundry.Deploy.csproj.");
        }

        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        string localWorkspace = Path.Combine(workingDirectoryPath, "FoundryDeployLocal");
        string publishDirectory = Path.Combine(localWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(localWorkspace, $"Foundry.Deploy-{runtimeIdentifier}.zip");

        TryDeleteDirectory(publishDirectory);
        Directory.CreateDirectory(publishDirectory);

        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare local workspace for Foundry.Deploy archive generation. ArchivePath={ArchivePath}", archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to prepare local workspace for Foundry.Deploy archive generation.",
                ex.ToString());
        }

        string publishArgs = string.Join(" ",
            "publish",
            WinPeProcessRunner.Quote(projectPath),
            "-c", "Release",
            "-r", runtimeIdentifier,
            "--self-contained", "true",
            "/p:PublishSingleFile=true",
            "/p:EnableCompressionInSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:IncludeAllContentForSelfExtract=true",
            "/p:DebugType=None",
            "/p:GenerateDocumentationFile=false",
            "-o", WinPeProcessRunner.Quote(publishDirectory));

        WinPeProcessExecution publish = await _processRunner.RunAsync(
            "dotnet",
            publishArgs,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!publish.IsSuccess)
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to publish Foundry.Deploy for local WinPE embedding.",
                publish.ToDiagnosticText());
        }

        string executablePath = Path.Combine(publishDirectory, "Foundry.Deploy.exe");
        if (!File.Exists(executablePath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Foundry.Deploy publish output is incomplete.",
                $"Expected executable: '{executablePath}'.");
        }

        try
        {
            ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Foundry.Deploy archive from publish output. PublishDirectory={PublishDirectory}, ArchivePath={ArchivePath}", publishDirectory, archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to create Foundry.Deploy archive from local publish output.",
                ex.ToString());
        }

        return WinPeResult<string>.Success(archivePath);
    }

    private static bool TryFindFoundryDeployProjectPath(out string projectPath)
    {
        foreach (string root in GetProjectDiscoveryRoots())
        {
            if (TryResolveFoundryDeployProjectPath(root, out projectPath))
            {
                return true;
            }
        }

        projectPath = string.Empty;
        return false;
    }

    private static IEnumerable<string> GetProjectDiscoveryRoots()
    {
        string current = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory) && !baseDirectory.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseDirectory;
        }
    }

    private static bool TryResolveFoundryDeployProjectPath(string startDirectory, out string projectPath)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            current = current.Parent;
        }

        projectPath = string.Empty;
        return false;
    }

    private static bool IsEnabledEnvironmentFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false
        };
    }

    private async Task<WinPeResult> RunRemediationIfConfiguredAsync(bool enabled, string? scriptPath, WinPeBuildArtifact artifact, WinPeToolPaths tools, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return WinPeResult.Failure(WinPeErrorCodes.BootExUnsupported, "PCA2023 requires /bootex or remediation fallback.");
        }

        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            return WinPeResult.Failure(WinPeErrorCodes.BootExUnsupported, "Remediation script was not found.", $"Path: '{scriptPath ?? "<null>"}'.");
        }

        string args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {WinPeProcessRunner.Quote(scriptPath)} -MediaPath {WinPeProcessRunner.Quote(artifact.MediaDirectoryPath)}";
        WinPeProcessExecution run = await _processRunner.RunAsync(tools.PowerShellPath, args, artifact.WorkingDirectoryPath, cancellationToken).ConfigureAwait(false);
        return run.IsSuccess
            ? WinPeResult.Success()
            : WinPeResult.Failure(WinPeErrorCodes.PcaRemediationFailed, "PCA2023 remediation fallback failed.", run.ToDiagnosticText());
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

    private static string NormalizeWinPeLanguageCode(string languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private static bool TryResolveInputLocale(string languageCode, out string canonicalLanguageCode, out string inputLocale)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageCode);
            canonicalLanguageCode = culture.Name;
            int keyboardLayoutId = culture.KeyboardLayoutId;
            string hex = keyboardLayoutId.ToString("x4", CultureInfo.InvariantCulture);
            inputLocale = $"{hex}:0000{hex}";
            return true;
        }
        catch (CultureNotFoundException)
        {
            canonicalLanguageCode = languageCode;
            inputLocale = string.Empty;
            return false;
        }
    }

}

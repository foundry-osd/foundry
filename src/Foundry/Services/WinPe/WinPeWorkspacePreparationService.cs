using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeWorkspacePreparationService : IWinPeWorkspacePreparationService
{
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly WinPeDriverPackageService _driverPackageService;
    private readonly IWinPeLocalDeployEmbeddingService _localDeployEmbeddingService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeWorkspacePreparationService> _logger;

    public WinPeWorkspacePreparationService(
        IWinPeDriverCatalogService driverCatalogService,
        IWinPeDriverInjectionService driverInjectionService,
        WinPeDriverPackageService driverPackageService,
        IWinPeLocalDeployEmbeddingService localDeployEmbeddingService,
        WinPeToolResolver toolResolver,
        WinPeProcessRunner processRunner,
        ILogger<WinPeWorkspacePreparationService> logger)
    {
        _driverCatalogService = driverCatalogService;
        _driverInjectionService = driverInjectionService;
        _driverPackageService = driverPackageService;
        _localDeployEmbeddingService = localDeployEmbeddingService;
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationRequest request,
        IProgress<WinPeWorkspacePreparationStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        progress?.Report(WinPeWorkspacePreparationStage.ResolvingDrivers);
        WinPeResult<IReadOnlyList<string>> drivers = await ResolveDriversAsync(
            request.DriverCatalogUri,
            request.Artifact.Architecture,
            request.DriverVendors,
            request.CustomDriverDirectoryPath,
            request.Artifact,
            request.Tools,
            cancellationToken).ConfigureAwait(false);
        if (!drivers.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(drivers.Error!);
        }

        _logger.LogInformation(
            "Resolved {DriverDirectoryCount} driver directory path(s). WorkingDirectoryPath={WorkingDirectoryPath}",
            drivers.Value!.Count,
            request.Artifact.WorkingDirectoryPath);

        progress?.Report(WinPeWorkspacePreparationStage.CustomizingImage);
        _logger.LogInformation(
            "Starting WinPE image customization. WorkingDirectoryPath={WorkingDirectoryPath}, DriverDirectoryCount={DriverDirectoryCount}, WinPeLanguage={WinPeLanguage}",
            request.Artifact.WorkingDirectoryPath,
            drivers.Value!.Count,
            request.WinPeLanguage);
        WinPeResult customize = await CustomizeImageAsync(
            request.Artifact,
            request.Tools,
            drivers.Value!,
            request.WinPeLanguage,
            request.ExpertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!customize.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(customize.Error!);
        }

        _logger.LogInformation(
            "WinPE image customization completed. WorkingDirectoryPath={WorkingDirectoryPath}",
            request.Artifact.WorkingDirectoryPath);

        progress?.Report(WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy);
        bool useBootEx = false;
        if (request.SignatureMode == WinPeSignatureMode.Pca2023)
        {
            _logger.LogInformation(
                "Evaluating PCA2023 signature policy. WorkingDirectoryPath={WorkingDirectoryPath}",
                request.Artifact.WorkingDirectoryPath);
            useBootEx = await _toolResolver.IsBootExSupportedAsync(
                request.Tools,
                _processRunner,
                request.Artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("PCA2023 signature policy evaluated. BootExSupported={BootExSupported}", useBootEx);
            if (!useBootEx)
            {
                return WinPeResult<WinPeWorkspacePreparationResult>.Failure(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.BootExUnsupported,
                        "PCA2023 requires /bootex support in the WinPE workspace."));
            }
        }

        return WinPeResult<WinPeWorkspacePreparationResult>.Success(new WinPeWorkspacePreparationResult
        {
            UseBootEx = useBootEx
        });
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

            bool hasInf = Directory.EnumerateFiles(normalizedCustomDirectory, "*.inf", SearchOption.AllDirectories).Any();
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
        string? expertDeployConfigurationJson,
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
        WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(
            _processRunner,
            tools.DismPath,
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
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

        WinPeResult localDeployProvisioning = await _localDeployEmbeddingService.ProvisionAsync(
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
        WinPeResult deployConfigurationProvisioning = await ProvisionDeployConfigurationInImageAsync(
            session.MountDirectoryPath,
            expertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!deployConfigurationProvisioning.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return deployConfigurationProvisioning;
        }

        if (!string.IsNullOrWhiteSpace(expertDeployConfigurationJson))
        {
            _logger.LogInformation("Provisioned Foundry.Deploy expert configuration into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

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

    private async Task<WinPeResult> ProvisionDeployConfigurationInImageAsync(
        string mountedImagePath,
        string? expertDeployConfigurationJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expertDeployConfigurationJson))
        {
            return WinPeResult.Success();
        }

        string destinationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedDeployConfigPathInImage);
        string? destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for Foundry.Deploy expert configuration.",
                $"Destination file: '{destinationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            await File.WriteAllTextAsync(
                destinationPath,
                expertDeployConfigurationJson,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Foundry.Deploy expert configuration into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry.Deploy expert configuration into mounted WinPE image.",
                ex.ToString());
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

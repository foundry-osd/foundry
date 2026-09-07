// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.Catalog;

namespace Foundry.Core.Services.Media;

/// <summary>Owns one captured media request from workspace acquisition through native cleanup.</summary>
public sealed class MediaOperationCoordinator
{
    private readonly IWinPeBuildService buildService;
    private readonly IWinPeWorkspacePreparationService preparationService;
    private readonly IWinPeRuntimePayloadProvisioningService runtimeService;
    private readonly IWinPeIsoMediaService isoService;
    private readonly IWinPeUsbMediaService usbService;
    private readonly IWinPeEmbeddedAssetService embeddedAssets;
    private readonly IConnectConfigurationGenerator connectGenerator;
    private readonly IDeployConfigurationGenerator deployGenerator;
    private readonly Func<string?, WinPeArchitecture, WinPeResult<WinPeToolPaths>> resolveTools;
    private readonly Func<CancellationToken, Task<IReadOnlyList<VerifiedCatalogDocument>>> acquireCatalogs;
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private Task<WinPeResult<MediaOperationResult>>? activeOperation;
    private WinPeDiagnostic? recovery;
    private WinPePreparedRuntimePayloads? retainedRuntime;
    private bool closeRequested;
    public string? LastDismDiagnosticLogPath { get; private set; }

    public MediaOperationCoordinator(IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService preparationService,
        IWinPeRuntimePayloadProvisioningService runtimeService,
        IWinPeIsoMediaService isoService, IWinPeUsbMediaService usbService,
        IWinPeEmbeddedAssetService embeddedAssets, IConnectConfigurationGenerator connectGenerator,
        IDeployConfigurationGenerator deployGenerator)
        : this(buildService, preparationService, runtimeService, isoService, usbService, embeddedAssets,
            connectGenerator, deployGenerator, (root, architecture) => new WinPeToolResolver().ResolveTools(root, architecture))
    { }

    internal MediaOperationCoordinator(IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService preparationService,
        IWinPeRuntimePayloadProvisioningService runtimeService,
        IWinPeIsoMediaService isoService, IWinPeUsbMediaService usbService,
        IWinPeEmbeddedAssetService embeddedAssets, IConnectConfigurationGenerator connectGenerator,
        IDeployConfigurationGenerator deployGenerator,
        Func<string?, WinPeArchitecture, WinPeResult<WinPeToolPaths>> resolveTools,
        Func<CancellationToken, Task<IReadOnlyList<VerifiedCatalogDocument>>>? acquireCatalogs = null)
    {
        this.buildService = buildService;
        this.preparationService = preparationService;
        this.runtimeService = runtimeService;
        this.isoService = isoService;
        this.usbService = usbService;
        this.embeddedAssets = embeddedAssets;
        this.connectGenerator = connectGenerator;
        this.deployGenerator = deployGenerator;
        this.resolveTools = resolveTools;
        this.acquireCatalogs = acquireCatalogs ?? AcquireCatalogsAsync;
    }

    public bool IsRunning { get { lock (sync) { return activeOperation is { IsCompleted: false }; } } }
    public Task? ActiveOperation { get { lock (sync) { return activeOperation; } } }
    public WinPeDiagnostic? RecoveryDiagnostic { get { lock (sync) { return recovery; } } }

    public Task<WinPeResult<MediaOperationResult>> RunAsync(MediaOperationRequest request,
        IProgress<WinPeWorkspacePreparationStage>? preparationProgress = null,
        IProgress<WinPeMountedImageCustomizationProgress>? customizationProgress = null,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        IProgress<WinPeMediaProgress>? mediaProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var completion = new TaskCompletionSource<WinPeResult<MediaOperationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (sync)
        {
            if (closeRequested || activeOperation is { IsCompleted: false } || recovery is not null)
            {
                request.Dispose();
                return Task.FromResult(WinPeResult<MediaOperationResult>.Failure(recovery ??
                    new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "A media operation is running or application close is pending.")));
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = cancellation.Token;
            activeOperation = completion.Task;
        }

        _ = ExecuteAsync(request, preparationProgress, customizationProgress, downloadProgress, mediaProgress, token, completion);
        return completion.Task;
    }

    /// <summary>Cancels managed work and joins owned cleanup; timeout never grants permission to close.</summary>
    public async Task<WinPeResult> RequestCancellationAndWaitAsync(CancellationToken waitToken)
    {
        Task<WinPeResult<MediaOperationResult>>? operation;
        CancellationTokenSource? source;
        lock (sync)
        {
            closeRequested = true;
            source = cancellation;
            operation = activeOperation;
        }
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }

        if (operation is not null)
        {
            await operation.WaitAsync(waitToken).ConfigureAwait(false);
        }

        return RecoveryDiagnostic is { } diagnostic ? WinPeResult.Failure(diagnostic) : WinPeResult.Success();
    }

    /// <summary>Restores admission only when the shell abandons its close request.</summary>
    public void ResumeAfterCancelledClose()
    {
        lock (sync) { closeRequested = false; }
    }

    private async Task ExecuteAsync(MediaOperationRequest request,
        IProgress<WinPeWorkspacePreparationStage>? preparationProgress,
        IProgress<WinPeMountedImageCustomizationProgress>? customizationProgress,
        IProgress<WinPeDownloadProgress>? downloadProgress, IProgress<WinPeMediaProgress>? mediaProgress,
        CancellationToken token, TaskCompletionSource<WinPeResult<MediaOperationResult>> completion)
    {
        MediaOperationLease? lease = null;
        WinPePreparedRuntimePayloads? runtime = null;
        WinPeResult<MediaOperationResult> result;
        WinPeDiagnostic? cleanupWarning = null;
        IReadOnlyList<string> warnings = [];
        Foundry.Core.Services.Diagnostics.DismDiagnosticScope? diagnostics = null;
        try
        {
            token.ThrowIfCancellationRequested();
            MediaPreflightOptions options = request.Options with
            {
                DriverVendors = request.Options.DriverVendors.ToArray(),
                AvailableWinPeLanguages = request.Options.AvailableWinPeLanguages.ToArray()
            };
            if (!Enum.IsDefined(request.Target) || (request.Target != MediaOperationTarget.Iso && options.SelectedUsbDisk is null))
            {
                throw new MediaFailure(new WinPeDiagnostic(WinPeErrorCodes.ValidationFailed, "A valid media target is required."));
            }

            lease = MediaOperationLease.Acquire(request.WorkspaceRoot, "Media", request.OperationId);
            diagnostics = new(Path.Combine(request.WorkspaceRoot, "Diagnostics"));
            LastDismDiagnosticLogPath = null;
            WinPeToolPaths tools = Value(resolveTools(request.AdkRootPath, options.Architecture));
            WinPeBuildArtifact artifact = Value(await buildService.BuildAsync(new WinPeBuildOptions
            {
                OutputDirectoryPath = lease.WorkingDirectoryPath,
                WorkingDirectoryPath = Path.Combine(lease.WorkingDirectoryPath, "WinPe"),
                AdkRootPath = tools.KitsRootPath,
                Architecture = options.Architecture,
                SignatureMode = options.SignatureMode
            }, token).ConfigureAwait(false));
            var runtimeOptions = request.RuntimePayloads with
            {
                Architecture = options.Architecture,
                WorkingDirectoryPath = lease.WorkingDirectoryPath,
                MountedImagePath = artifact.MountDirectoryPath,
                UsbCacheRootPath = string.Empty
            };
            runtime = Value(await runtimeService.PrepareAsync(runtimeOptions, downloadProgress, token).ConfigureAwait(false));
            IReadOnlyList<VerifiedCatalogDocument> catalogs = await acquireCatalogs(token).ConfigureAwait(false);
            if (!catalogs.Any(catalog => catalog.Id == VerifiedCatalogSources.DriverPacks))
                warnings = ["The optional OEM catalog snapshot is unavailable. Offline OEM selection requires refreshing this medium; online catalog loading remains available."];
            WinPeMediaManifest manifest = WinPeMediaManifestStore.Create(runtime, request.Target,
                request.Target == MediaOperationTarget.Iso ? null : CreateUsbOptions(options, runtimeOptions, runtime, downloadProgress, mediaProgress).ExpectedDisk,
                catalogs);
            FoundryConnectProvisioningBundle connect = connectGenerator.CreateProvisioningBundle(
                request.Configuration.ConnectDocument with { Telemetry = request.ConnectTelemetry },
                Path.Combine(lease.WorkingDirectoryPath, "Provisioning"));
            WinPeWorkspacePreparationResult workspace;
            try
            {
                workspace = Value(await preparationService.PrepareAsync(new WinPeWorkspacePreparationOptions
                {
                    Artifact = artifact,
                    Tools = tools,
                    SignatureMode = options.SignatureMode,
                    BootImageSource = options.BootImageSource,
                    DriverCatalogUri = new WinPeDriverCatalogOptions().CatalogUri,
                    DriverVendors = options.DriverVendors,
                    CustomDriverDirectoryPath = options.CustomDriverDirectoryPath,
                    WinPeLanguage = options.WinPeLanguage,
                    AssetProvisioning = CreateAssets(request, options, tools, connect) with { MediaManifest = manifest, VerifiedCatalogDocuments = catalogs },
                    RuntimePayloadProvisioning = runtimeOptions,
                    PreparedRuntime = runtime,
                    MediaManifest = manifest,
                    VerifiedCatalogDocuments = catalogs,
                    WinReCacheDirectoryPath = request.WinReCacheDirectoryPath,
                    Progress = preparationProgress,
                    DownloadProgress = downloadProgress,
                    CustomizationProgress = customizationProgress
                }, token).ConfigureAwait(false));
            }
            finally
            {
                if (connect.MediaSecretsKey is not null)
                {
                    CryptographicOperations.ZeroMemory(connect.MediaSecretsKey);
                }
            }

            if (request.Target == MediaOperationTarget.Iso)
            {
                WinPeResult published = await isoService.CreateAsync(new WinPeIsoMediaOptions
                {
                    PreparedWorkspace = workspace,
                    OutputIsoPath = options.IsoOutputPath,
                    IsoTempDirectoryPath = Path.Combine(lease.WorkingDirectoryPath, "Iso"),
                    Progress = mediaProgress
                }, token).ConfigureAwait(false);
                Check(published);
                cleanupWarning = published.CleanupDiagnostic;
                result = WinPeResult<MediaOperationResult>.Success(new(lease.OperationId, request.Target, options.IsoOutputPath));
            }
            else
            {
                UsbOutputOptions usbOptions = CreateUsbOptions(options, runtimeOptions, runtime, downloadProgress, mediaProgress);
                WinPeResult<WinPeUsbProvisionResult> usb = request.Target == MediaOperationTarget.UsbUpdate
                    ? await usbService.UpdateBootPartitionAsync(usbOptions, workspace.Artifact, tools, workspace.UseBootEx, token).ConfigureAwait(false)
                    : await usbService.ProvisionAndPopulateAsync(usbOptions, workspace.Artifact, tools, workspace.UseBootEx, token).ConfigureAwait(false);
                Check(usb);
                cleanupWarning = usb.CleanupDiagnostic;
                result = WinPeResult<MediaOperationResult>.Success(new(lease.OperationId, request.Target, UsbResult: usb.Value));
            }
        }
        catch (Exception ex)
        {
            WinPeDiagnostic diagnostic = ex is MediaFailure failure ? failure.Diagnostic : new WinPeDiagnostic(
                ex is OperationCanceledException ? WinPeErrorCodes.OperationCancelled : WinPeErrorCodes.InternalError,
                ex is OperationCanceledException ? "Media creation was cancelled." : "Media creation failed.",
                details: ex.Message, exception: ex);
            if (HasUncertainNativeOwnership(diagnostic.Exception))
            {
                diagnostic = diagnostic with { RecoveryRequired = true };
            }
            result = WinPeResult<MediaOperationResult>.Failure(diagnostic);
        }

        try
        {
            if (lease is not null && result.Error is { RecoveryRequired: true } error)
            {
                lock (sync)
                {
                    recovery = error with { RetainedPaths = error.RetainedPaths.Append(lease.WorkingDirectoryPath).Distinct().ToArray() };
                    retainedRuntime = runtime;
                }
                runtime = null;
                lease.RetainForRecovery(recovery.RetainedPaths);
                result = WinPeResult<MediaOperationResult>.Failure(recovery);
            }
            else
            {
                runtime?.Dispose();
                runtime = null;
                lease?.DeleteOwnedWorkspace();
            }
        }
        catch (Exception ex)
        {
            cleanupWarning = new WinPeDiagnostic(WinPeErrorCodes.InternalError,
                "The operation workspace could not be removed.", lease?.WorkingDirectoryPath, exception: ex)
            { RetainedPaths = lease is null ? [] : [lease.WorkingDirectoryPath] };
            if (result.Error is { } primary)
            {
                result = WinPeResult<MediaOperationResult>.Failure(primary with { CleanupDiagnostic = cleanupWarning });
            }
        }
        finally
        {
            diagnostics?.Dispose();
            LastDismDiagnosticLogPath = diagnostics?.CapturedLogPath;
            request.Dispose();
            try { lease?.Dispose(); }
            catch (Exception ex)
            {
                cleanupWarning ??= new WinPeDiagnostic(WinPeErrorCodes.InternalError, "The operation journal could not be released.", exception: ex);
            }
            lock (sync)
            {
                cancellation?.Dispose();
                cancellation = null;
            }
        }

        if (result.IsSuccess)
        {
            result = WinPeResult<MediaOperationResult>.SuccessWithCleanup(result.Value! with
            { Warnings = warnings, DismDiagnosticLogPath = diagnostics?.CapturedLogPath }, cleanupWarning);
        }
        completion.TrySetResult(result);
    }

    private WinPeMountedImageAssetProvisioningOptions CreateAssets(MediaOperationRequest request,
        MediaPreflightOptions options, WinPeToolPaths tools, FoundryConnectProvisioningBundle connect)
    {
        string? oa3 = options.IsAutopilotEnabled && options.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload
            ? Value(WinPeOa3ToolResolver.Resolve(tools.KitsRootPath, options.Architecture)) : null;
        return new WinPeMountedImageAssetProvisioningOptions
        {
            BootstrapScriptContent = embeddedAssets.GetBootstrapScriptContent(),
            SevenZipSourceDirectoryPath = embeddedAssets.GetSevenZipSourceDirectoryPath(),
            IanaWindowsTimeZoneMapJson = embeddedAssets.GetIanaWindowsTimeZoneMapJson(),
            FoundryConnectConfigurationJson = connect.ConfigurationJson,
            DeployConfigurationJson = deployGenerator.Serialize(deployGenerator.Generate(
                request.Configuration.DeployDocument with { Telemetry = request.DeployTelemetry },
                request.Protection.DeploymentKey, request.Protection.Settings, request.Configuration.OobeAccountSecrets)),
            NetworkSecretsKey = connect.MediaSecretsKey,
            DeploymentSecretsKey = request.Protection.DeploymentKey,
            IsDeploymentProtectionEnabled = request.Protection.Settings.IsEnabled,
            Unattend = request.Configuration.Configuration.Unattend,
            FoundryConnectAssetFiles = connect.AssetFiles,
            AutopilotProvisioningMode = options.IsAutopilotEnabled ? options.AutopilotProvisioningMode : AutopilotProvisioningMode.JsonProfile,
            Oa3ToolSourcePath = oa3,
            AutopilotProfiles = options.IsAutopilotEnabled && options.AutopilotProvisioningMode == AutopilotProvisioningMode.JsonProfile
                ? request.Configuration.Configuration.Autopilot.Profiles : [],
            ConnectProvisioningSource = request.RuntimePayloads.Connect.IsEnabled ? request.RuntimePayloads.Connect.ProvisioningSource : WinPeProvisioningSource.Release,
            DeployProvisioningSource = request.RuntimePayloads.Deploy.IsEnabled ? request.RuntimePayloads.Deploy.ProvisioningSource : WinPeProvisioningSource.Release
        };
    }

    private static UsbOutputOptions CreateUsbOptions(MediaPreflightOptions options,
        WinPeRuntimePayloadProvisioningOptions runtimeOptions, WinPePreparedRuntimePayloads runtime,
        IProgress<WinPeDownloadProgress>? download, IProgress<WinPeMediaProgress>? progress)
    {
        WinPeUsbDiskCandidate disk = options.SelectedUsbDisk!;
        return new UsbOutputOptions
        {
            TargetDiskNumber = disk.DiskNumber,
            ExpectedDisk = new WinPeUsbDiskIdentity
            {
                Number = disk.DiskNumber,
                FriendlyName = disk.FriendlyName,
                SerialNumber = disk.SerialNumber,
                UniqueId = disk.UniqueId,
                BusType = disk.BusType,
                IsRemovable = disk.IsRemovable,
                IsSystem = disk.IsSystem,
                IsBoot = disk.IsBoot,
                IsOffline = disk.IsOffline,
                IsReadOnly = disk.IsReadOnly,
                Size = disk.SizeBytes
            },
            PartitionStyle = options.UsbPartitionStyle,
            FormatMode = options.UsbFormatMode,
            RuntimePayloadProvisioning = runtimeOptions with { MountedImagePath = string.Empty },
            PreparedRuntime = runtime,
            DownloadProgress = download,
            Progress = progress
        };
    }

    private static async Task<IReadOnlyList<VerifiedCatalogDocument>> AcquireCatalogsAsync(CancellationToken token)
    {
        var acquirer = new VerifiedCatalogSnapshotAcquirer();
        Task<VerifiedCatalogDocument> operatingSystems = acquirer.AcquireAsync(VerifiedCatalogSources.OperatingSystems, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems), token);
        Task<VerifiedCatalogDocument?> drivers = AcquireOptionalDriversAsync(acquirer, token);
        await Task.WhenAll(new Task[] { operatingSystems, drivers }).ConfigureAwait(false);
        return drivers.Result is { } available ? [operatingSystems.Result, available] : [operatingSystems.Result];
    }

    private static async Task<VerifiedCatalogDocument?> AcquireOptionalDriversAsync(VerifiedCatalogSnapshotAcquirer acquirer, CancellationToken token)
    {
        try { return await acquirer.AcquireAsync(VerifiedCatalogSources.DriverPacks, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.DriverPacks), token).ConfigureAwait(false); }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or IOException or TimeoutException or System.Xml.XmlException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static bool HasUncertainNativeOwnership(Exception? error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if ((current.Data.Contains("ProcessRootExitConfirmed") || current.Data.Contains("ProcessTreeTerminationConfirmed")) &&
                !(current.Data["ProcessRootExitConfirmed"] is true && current.Data["ProcessTreeTerminationConfirmed"] is true))
            {
                return true;
            }
        }
        return false;
    }

    private static T Value<T>(WinPeResult<T> result) { Check(result); return result.Value!; }
    private static void Check(WinPeResult result)
    {
        if (!result.IsSuccess) { throw new MediaFailure(result.Error!); }
    }
    private sealed class MediaFailure(WinPeDiagnostic diagnostic) : Exception(diagnostic.Message, diagnostic.Exception)
    {
        public WinPeDiagnostic Diagnostic { get; } = diagnostic;
    }
}

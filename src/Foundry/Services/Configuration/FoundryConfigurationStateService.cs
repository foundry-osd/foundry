// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Autopilot;
using Foundry.Telemetry;
using Foundry.Utilities.Security;
using Serilog;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AppSettingsService = Foundry.Services.Settings.IAppSettingsService;

namespace Foundry.Services.Configuration;

/// <summary>
/// Maintains authoring configuration, readiness checks and immutable build capture.
/// </summary>
/// <remarks>
/// Passwords never enter the persisted document: they are kept in the secret state services, remembered in the local vault by
/// <see cref="Foundry.Services.Secrets.SecretVaultBridge"/>, and passed beside the document, never merged into it, when media is
/// validated or generated.
/// </remarks>
internal sealed class FoundryConfigurationStateService : IFoundryConfigurationStateService
{
    private readonly IFoundryConfigurationService foundryConfigurationService;
    private readonly IDeployConfigurationGenerator deployConfigurationGenerator;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService;
    private readonly IOobeAccountSecretStateService oobeAccountSecretStateService;
    private readonly IDomainJoinSecretStateService domainJoinSecretStateService;
    private readonly IAutopilotHardwareHashSessionState autopilotHardwareHashSessionState;
    private readonly AppSettingsService appSettingsService;
    private readonly ILogger logger;
    private readonly Foundry.Core.Services.Images.CustomImageLibraryService customImageLibrary;
    private readonly Foundry.Core.Services.Packages.ContentPackageLibraryService postInstallPackages;
    private readonly Foundry.Core.Services.Drivers.CustomDriverSetLibrary customDriverSetLibrary;
    private readonly Foundry.Core.Services.Files.AuthoringFileLibrary authoringFiles;
    private readonly Foundry.Core.Services.Drivers.CustomDriverSetAvailabilityCache customDriverAvailability = new();
    private UnattendSettings? validatedUnattendSettings;
    private IReadOnlyList<UnattendSourceValidation> unattendSourceValidations = [];
    private readonly List<(UnattendSettings Settings, Task<IReadOnlyList<UnattendSourceValidation>> Read)> unattendSourceReads = [];
    private long unattendRefreshRevision;

    public FoundryConfigurationStateService(
        IFoundryConfigurationService foundryConfigurationService,
        IDeployConfigurationGenerator deployConfigurationGenerator,
        INetworkSecretStateService networkSecretStateService,
        IDeploymentProtectionSecretStateService deploymentProtectionSecretStateService,
        IOobeAccountSecretStateService oobeAccountSecretStateService,
        IDomainJoinSecretStateService domainJoinSecretStateService,
        IAutopilotHardwareHashSessionState autopilotHardwareHashSessionState,
        AppSettingsService appSettingsService,
        ILogger logger,
        Foundry.Core.Services.Images.CustomImageLibraryService customImageLibrary,
        Foundry.Core.Services.Packages.ContentPackageLibraryService postInstallPackages,
        Foundry.Core.Services.Drivers.CustomDriverSetLibrary customDriverSetLibrary,
        Foundry.Core.Services.Files.AuthoringFileLibrary authoringFiles)
    {
        this.foundryConfigurationService = foundryConfigurationService;
        this.deployConfigurationGenerator = deployConfigurationGenerator;
        this.networkSecretStateService = networkSecretStateService;
        this.deploymentProtectionSecretStateService = deploymentProtectionSecretStateService;
        this.oobeAccountSecretStateService = oobeAccountSecretStateService;
        this.domainJoinSecretStateService = domainJoinSecretStateService;
        this.autopilotHardwareHashSessionState = autopilotHardwareHashSessionState;
        this.appSettingsService = appSettingsService;
        this.logger = logger.ForContext<FoundryConfigurationStateService>();
        this.customImageLibrary = customImageLibrary;
        this.postInstallPackages = postInstallPackages;
        this.customDriverSetLibrary = customDriverSetLibrary;
        this.authoringFiles = authoringFiles;
        FoundryConfigurationDocument loaded = Load();
        loaded = FoundryConfigurationMigration.MigrateDefaultIsoOutput(loaded, Constants.LegacyDefaultIsoPath, Constants.DefaultIsoPath);
        Current = SanitizeForPersistence(loaded);
        SavedConfiguration = Current;
        Save();
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public FoundryConfigurationDocument Current { get; private set; }

    /// <inheritdoc />
    public FoundryConfigurationDocument SavedConfiguration { get; private set; }

    /// <inheritdoc />
    public long ReplacementVersion { get; private set; }

    /// <inheritdoc />
    public bool IsManaged => ManagedBy is not null;

    /// <inheritdoc />
    public string? ManagedBy { get; private set; }

    /// <inheritdoc />
    public event EventHandler? ManagedChanged;

    /// <inheritdoc />
    public void SetManaged(string managedBy)
    {
        ArgumentNullException.ThrowIfNull(managedBy);
        if (string.Equals(ManagedBy, managedBy, StringComparison.Ordinal)) return;
        ManagedBy = managedBy;
        ManagedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ClearManaged()
    {
        if (ManagedBy is null) return;
        ManagedBy = null;
        ManagedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool IsChangeAllowed(FoundryConfigurationDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return !IsManaged || Foundry.Core.Services.Sharing.ManagedConfigurationGuard.IsAllowed(SanitizeForPersistence(Current), SanitizeForPersistence(candidate));
    }

    /// <inheritdoc />
    public void Replace(FoundryConfigurationDocument document, Action restoreSecrets)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(restoreSecrets);
        FoundryConfigurationDocument candidate = SanitizeForPersistence(document);
        Save(candidate, throwOnFailure: true);
        restoreSecrets();
        networkSecretStateService.Update(document.Network);
        oobeAccountSecretStateService.Update(document.Customization.Oobe);
        domainJoinSecretStateService.Update(document.DomainJoin);
        Current = candidate;
        validatedUnattendSettings = null;
        unattendSourceValidations = [];
        unattendRefreshRevision++;
        ReplacementVersion++;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public NetworkMediaReadinessEvaluation NetworkMediaReadiness => EvaluateNetworkMediaReadiness();

    /// <inheritdoc />
    public bool IsNetworkConfigurationReady => NetworkMediaReadiness.IsNetworkConfigurationReady;

    /// <inheritdoc />
    public bool IsDeployConfigurationReady
    {
        get
        {
            if (!IsUnattendConfigurationReady || !IsCustomImagesReady || !IsPostInstallationReady || !IsCustomDriversReady)
            {
                return false;
            }

            if (Current.General.DeploymentProtection.IsEnabled &&
                !deploymentProtectionSecretStateService.IsValid)
            {
                return false;
            }

            if (!oobeAccountSecretStateService.Validate(Current.Customization.Oobe).IsValid)
            {
                return false;
            }

            try
            {
                byte[]? deploymentSecretsKey = Current.General.DeploymentProtection.IsEnabled ||
                                               Current.Autopilot.IsEnabled &&
                                               Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload
                    ? AesGcmEncryption.GenerateKey()
                    : null;
                DeployProtectionSettings? protectionSettings = Current.General.DeploymentProtection.IsEnabled
                    ? new DeployProtectionSettings
                    {
                        IsEnabled = true
                    }
                    : null;

                try
                {
                    _ = GenerateDeployConfigurationJson(
                        deploymentSecretsKey: deploymentSecretsKey,
                        protectionSettings: protectionSettings);
                    return true;
                }
                finally
                {
                    if (deploymentSecretsKey is not null)
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(deploymentSecretsKey);
                    }
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool IsConnectProvisioningReady => NetworkMediaReadiness.IsConnectProvisioningReady;

    /// <inheritdoc />
    public bool AreRequiredSecretsReady => NetworkMediaReadiness.AreRequiredSecretsReady;

    /// <inheritdoc />
    public bool IsAutopilotEnabled => Current.Autopilot.IsEnabled;

    /// <inheritdoc />
    public bool IsAutopilotConfigurationReady => AutopilotConfigurationValidation.IsReady;

    /// <inheritdoc />
    public bool IsDomainJoinConfigurationReady => DomainJoinConfigurationValidator.EvaluateReadiness(
        Current.DomainJoin,
        domainJoinSecretStateService.HasPassword,
        Current.General.DeploymentProtection.IsEnabled && deploymentProtectionSecretStateService.IsValid).IsValid;

    /// <inheritdoc />
    public bool IsUnattendConfigurationReady
    {
        get
        {
            try
            {
                UnattendFileService.ValidateSettings(Current.Unattend, Current.General.DeploymentProtection.IsEnabled);
                return !Current.Unattend.IsEnabled ||
                    (ReferenceEquals(validatedUnattendSettings, Current.Unattend) &&
                     unattendSourceValidations.Count == Current.Unattend.Files.Count &&
                     unattendSourceValidations.All(result => result.Inspection is not null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UnattendSourceValidation> UnattendSourceValidations =>
        ReferenceEquals(validatedUnattendSettings, Current.Unattend) ? unattendSourceValidations : [];

    /// <inheritdoc />
    public async Task RefreshUnattendSourcesAsync()
    {
        UnattendSettings settings = Current.Unattend;
        long revision = ++unattendRefreshRevision;
        validatedUnattendSettings = null;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (!ReferenceEquals(Current.Unattend, settings))
            {
                return;
            }

            // Keep one replacement slot so removing a hung source can recover without spawning unlimited workers.
            unattendSourceReads.RemoveAll(scan => scan.Read.IsCompleted);
            Task<IReadOnlyList<UnattendSourceValidation>>? sourceRead = unattendSourceReads
                .FirstOrDefault(scan => ReferenceEquals(scan.Settings, settings)).Read;
            if (sourceRead is null)
            {
                if (unattendSourceReads.Count >= 2)
                {
                    PublishUnattendSourceResults(settings, revision, (settings.Files ?? []).Select(file => new UnattendSourceValidation(
                        file, null, "Two answer-file checks are still running.")).ToArray());
                    return;
                }

                sourceRead = Task.Run(() => InspectUnattendSources(settings, authoringFiles));
                unattendSourceReads.Add((settings, sourceRead));
            }

            IReadOnlyList<UnattendSourceValidation> results = await sourceRead.WaitAsync(TimeSpan.FromSeconds(15));
            PublishUnattendSourceResults(settings, revision, results);
        }
        catch (TimeoutException)
        {
            PublishUnattendSourceResults(settings, revision, (settings.Files ?? []).Select(file => new UnattendSourceValidation(
                file, null, "Answer-file checks timed out.")).ToArray());
        }
    }

    private void PublishUnattendSourceResults(UnattendSettings settings, long revision, IReadOnlyList<UnattendSourceValidation> results)
    {
        if (revision != unattendRefreshRevision || !ReferenceEquals(Current.Unattend, settings))
        {
            return;
        }

        validatedUnattendSettings = settings;
        unattendSourceValidations = results;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<UnattendSourceValidation> InspectUnattendSources(
        UnattendSettings settings,
        Foundry.Core.Services.Files.AuthoringFileLibrary files)
    {
        List<UnattendSourceValidation> results = [];
        foreach (UnattendFileSettings file in settings.Files ?? [])
        {
            byte[]? content = null;
            try
            {
                content = UnattendFileService.ReadValidated(file, file.File is null ? null : files.GetAvailablePath(file.File));
                results.Add(new UnattendSourceValidation(file, UnattendFileService.Inspect(content), null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                string message = ex is InvalidDataException or InvalidOperationException
                    ? ex.Message
                    : UnattendFileService.ContentUnavailableMessage;
                results.Add(new UnattendSourceValidation(file, null, message));
            }
            finally
            {
                if (content is not null)
                {
                    CryptographicOperations.ZeroMemory(content);
                }
            }
        }

        return results;
    }

    /// <inheritdoc />
    public AutopilotConfigurationValidationResult AutopilotConfigurationValidation =>
        AutopilotConfigurationValidator.Evaluate(Current.Autopilot, DateTimeOffset.UtcNow, !string.IsNullOrWhiteSpace(MediaInputs.AutopilotPfxPassword));

    /// <inheritdoc />
    public MediaInputs MediaInputs => new()
    {
        ResolveFile = authoringFiles.GetAvailablePath,
        Network = networkSecretStateService.GetSecrets(Current.Network),
        AutopilotPfxPassword = autopilotHardwareHashSessionState.GetBootMediaCertificatePassword(Current.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx)
    };

    /// <inheritdoc />
    public AutopilotProvisioningMode AutopilotProvisioningMode => Current.Autopilot.ProvisioningMode;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileDisplayName => Current.Autopilot.IsEnabled &&
                                                          Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
        ? GetSelectedAutopilotProfile()?.DisplayName
        : null;

    /// <inheritdoc />
    public string? SelectedAutopilotProfileFolderName => Current.Autopilot.IsEnabled &&
                                                         Current.Autopilot.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
        ? GetSelectedAutopilotProfile()?.FolderName
        : null;

    /// <inheritdoc />
    public void UpdateGeneral(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FoundryConfigurationDocument candidate = Current with { General = SanitizeGeneralForPersistence(settings) };
        ThrowIfManagedChange(candidate);
        Current = candidate;
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateLocalization(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { Localization = settings });
        Current = Current with { Localization = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateOperatingSystemSelection(OperatingSystemSelectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FoundryConfigurationDocument candidate = Current with { OperatingSystemSelection = OperatingSystemSelectionSettingsNormalizer.Normalize(settings) };
        ThrowIfManagedChange(candidate);
        Current = candidate;
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateNetwork(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { Network = settings });
        networkSecretStateService.Update(settings);
        Current = Current with { Network = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateCustomization(CustomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { Customization = SanitizeCustomizationForPersistence(settings) });
        oobeAccountSecretStateService.Update(settings.Oobe);
        Current = Current with { Customization = SanitizeCustomizationForPersistence(settings) };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateAutopilot(AutopilotSettings settings)
    {
        UpdateProvisioningSelection(settings, Current.DomainJoin);
    }

    /// <inheritdoc />
    public void UpdateDomainJoin(DomainJoinSettings settings) => UpdateProvisioningSelection(Current.Autopilot, settings);

    /// <inheritdoc />
    public void UpdateProvisioningSelection(AutopilotSettings autopilot, DomainJoinSettings domainJoin)
    {
        DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(autopilot, domainJoin);
        FoundryConfigurationDocument candidate = Current with { Autopilot = SanitizeAutopilotForPersistence(autopilot), DomainJoin = domainJoin };
        ThrowIfManagedChange(candidate);
        Save(candidate);
        Current = candidate;
        domainJoinSecretStateService.Update(domainJoin);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateUnattend(UnattendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { Unattend = settings });
        Current = Current with { Unattend = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateCustomImages(CustomImagesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { CustomImages = settings });
        Current = Current with { CustomImages = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void RefreshCustomImageReadiness() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void UpdatePreOobe(PreOobeSettings settings, bool requirePersistence = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FoundryConfigurationDocument candidate = Current with { PreOobe = settings };
        ThrowIfManagedChange(candidate);
        Save(SanitizeForPersistence(candidate), throwOnFailure: requirePersistence);
        Current = candidate;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool IsPostInstallationReady => PreOobeConfigurationValidator.IsReady(Current.PreOobe, postInstallPackages.IsAvailable);

    /// <inheritdoc />
    public void RefreshPostInstallationReadiness() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public bool IsCustomImagesReady => !Current.CustomImages.IsEnabled ||
        (Foundry.Core.Services.Images.CustomImageSettingsValidator.Validate(Current.CustomImages, requireIncludedImage: true).Count == 0 &&
        Current.CustomImages.Images.Where(image => image.IsIncluded).All(customImageLibrary.IsAvailable));

    /// <inheritdoc />
    public void UpdateCustomDrivers(CustomDriverSetsSettings settings, bool requirePersistence = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FoundryConfigurationDocument candidate = Current with { CustomDrivers = settings };
        ThrowIfManagedChange(candidate);
        Save(SanitizeForPersistence(candidate), throwOnFailure: requirePersistence);
        Current = candidate;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool IsCustomDriversReady => customDriverAvailability.IsReady(Current.CustomDrivers);

    /// <inheritdoc />
    public bool? GetCustomDriverSetAvailability(string contentHash) => customDriverAvailability.GetAvailability(contentHash);

    /// <inheritdoc />
    public async Task RefreshCustomDriversReadinessAsync()
    {
        ContentPackageReference[] packages = Current.CustomDrivers.Sets.Select(set => set.Package)
            .DistinctBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase).ToArray();
        await InspectCustomDriverSetsAsync(packages);
    }

    /// <inheritdoc />
    public Task InspectCustomDriverSetAsync(ContentPackageReference package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return InspectCustomDriverSetsAsync([package]);
    }

    private async Task InspectCustomDriverSetsAsync(ContentPackageReference[] packages)
    {
        try
        {
            await Task.Run(() => customDriverAvailability.Inspect(customDriverSetLibrary, packages));
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Custom driver set availability could not be checked. PackageCount={PackageCount}", packages.Length);
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void UpdateTelemetry(TelemetrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfManagedChange(Current with { Telemetry = settings });
        Current = Current with { Telemetry = settings };
        Save();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GenerateDeployConfigurationJson(
        TelemetrySettings? telemetryOverride = null,
        byte[]? deploymentSecretsKey = null,
        DeployProtectionSettings? protectionSettings = null)
    {
        FoundryConfigurationDocument document = CreateDocumentForDeployGeneration(telemetryOverride);
        OobeAccountConfigurationValidationResult validation = oobeAccountSecretStateService.Validate(document.Customization.Oobe);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("OOBE local account password confirmation is invalid.");
        }

        using OobeAccountSecretState oobeAccountSecretState = CreateOobeAccountSecretStateForDeployGeneration(document.Customization.Oobe);
        using DomainJoinSecretState domainSecrets = CreateDomainJoinSecretState(document.DomainJoin);

        return deployConfigurationGenerator.Serialize(
            deployConfigurationGenerator.Generate(document, deploymentSecretsKey, protectionSettings, oobeAccountSecretState, domainSecrets, MediaInputs));
    }

    /// <inheritdoc />
    public Task<BuildSnapshot> CaptureBuildSnapshotAsync(
        string privateRootDirectory,
        CancellationToken cancellationToken = default)
    {
        string? authoringVersion = typeof(FoundryApplicationInfo).Assembly.GetName().Version?.ToString(4);
        FoundryConfigurationDocument document = CreateDocumentForDeployGeneration(telemetryOverride: null);
        using OobeAccountSecretState accountSecrets = CreateOobeAccountSecretStateForDeployGeneration(document.Customization.Oobe);
        using DomainJoinSecretState domainSecrets = CreateDomainJoinSecretState(document.DomainJoin);
        char[] password = document.General.DeploymentProtection.IsEnabled
            ? deploymentProtectionSecretStateService.GetConfirmedPasswordCopy()
            : [];
        try
        {
            return BuildSnapshot.CaptureAsync(
                document, CreateSnapshotSources(), accountSecrets, domainSecrets, password, privateRootDirectory, authoringVersion, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    private BuildSnapshotSources CreateSnapshotSources()
    {
        MediaInputs inputs = MediaInputs;
        return new BuildSnapshotSources(authoringFiles) { Network = inputs.Network, AutopilotPfxPassword = inputs.AutopilotPfxPassword };
    }

    private DomainJoinSecretState CreateDomainJoinSecretState(DomainJoinSettings settings)
    {
        var state = new DomainJoinSecretState();
        try
        {
            foreach (string account in settings.GetReferencedAccountNames())
            {
                char[]? password = domainJoinSecretStateService.GetPasswordCopy(account);
                if (password is null) continue;
                try { state.SetPassword(account, password); }
                finally { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan())); }
            }

            return state;
        }
        catch
        {
            state.Dispose();
            throw;
        }
    }

    private FoundryConfigurationDocument Load()
    {
        string sourcePath = File.Exists(Constants.FoundryConfigurationStatePath)
            ? Constants.FoundryConfigurationStatePath : Constants.LegacyFoundryConfigurationStatePath;
        if (!File.Exists(sourcePath))
        {
            logger.Information("Foundry configuration state initialized from defaults.");
            return FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
                CreateDefaultDocument(),
                appSettingsService.MigratedGeneralSettings);
        }

        try
        {
            if (new FileInfo(sourcePath).Length > 16 * 1024 * 1024)
            {
                throw new NotSupportedException("The saved configuration exceeds the supported size.");
            }

            string json = File.ReadAllText(sourcePath);
            using System.Text.Json.JsonDocument parsed = System.Text.Json.JsonDocument.Parse(json);
            if (parsed.RootElement.TryGetProperty("schemaVersion", out System.Text.Json.JsonElement schema) &&
                schema.GetInt32() > FoundryConfigurationDocument.CurrentSchemaVersion)
            {
                throw new NotSupportedException("The saved configuration requires a newer version of Foundry.");
            }

            FoundryConfigurationDocument document = foundryConfigurationService.Deserialize(json);
            logger.Information("Foundry configuration state loaded from disk.");
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            string backupPath = Constants.FoundryConfigurationStatePath + ".invalid";
            if (sourcePath == Constants.FoundryConfigurationStatePath)
            {
                TryMoveInvalidState(backupPath, ex);
            }
            else
            {
                logger.Warning(ex, "Legacy Foundry configuration could not be loaded. Defaults were restored. SourceKind={SourceKind}", "Legacy");
            }
            return CreateDefaultDocument();
        }
    }

    private FoundryConfigurationDocument CreateDocumentForDeployGeneration(TelemetrySettings? telemetryOverride)
    {
        return Current with { Telemetry = telemetryOverride ?? Current.Telemetry };
    }

    private OobeAccountSecretState CreateOobeAccountSecretStateForDeployGeneration(OobeSettings settings)
    {
        var state = new OobeAccountSecretState();

        if (!settings.IsEnabled)
        {
            return state;
        }

        char[] administratorPassword = oobeAccountSecretStateService.GetAdministratorPasswordCopy();
        try
        {
            state.SetAdministratorPassword(administratorPassword);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(administratorPassword.AsSpan()));
        }

        char[] administratorConfirmation = oobeAccountSecretStateService.GetAdministratorConfirmationCopy();
        try
        {
            state.SetAdministratorConfirmation(administratorConfirmation);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(administratorConfirmation.AsSpan()));
        }

        foreach (OobeAdditionalAccountSettings account in settings.AdditionalAccounts)
        {
            char[] password = oobeAccountSecretStateService.GetAdditionalAccountPasswordCopy(account.Id);
            try
            {
                state.SetAdditionalAccountPassword(account.Id, password);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            }

            char[] confirmation = oobeAccountSecretStateService.GetAdditionalAccountConfirmationCopy(account.Id);
            try
            {
                state.SetAdditionalAccountConfirmation(account.Id, confirmation);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(confirmation.AsSpan()));
            }
        }

        return state;
    }

    private static FoundryConfigurationDocument CreateDefaultDocument()
    {
        return new FoundryConfigurationDocument
        {
            General = new GeneralSettings
            {
                IsoOutputPath = Constants.DefaultIsoPath
            }
        };
    }

    /// <summary>Refuses a change of a published section while a followed publication manages the configuration.</summary>
    /// <exception cref="InvalidOperationException">The change touches a value outside the per-PC values.</exception>
    private void ThrowIfManagedChange(FoundryConfigurationDocument candidate)
    {
        if (IsChangeAllowed(candidate))
        {
            return;
        }

        logger.Warning("A change to a published configuration section was refused because a followed publication manages the configuration.");
        throw new InvalidOperationException("Configuration.Managed");
    }

    private void Save(FoundryConfigurationDocument? candidate = null, bool throwOnFailure = false)
    {
        try
        {
            Directory.CreateDirectory(Constants.ConfigurationWorkspaceDirectoryPath);
            FoundryConfigurationDocument document = candidate ?? SanitizeForPersistence(Current);
            string json = foundryConfigurationService.Serialize(document);
            string temporaryPath = Constants.FoundryConfigurationStatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, Constants.FoundryConfigurationStatePath, true);
                SavedConfiguration = document;
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error(ex, "Failed to persist Foundry configuration state. StatePath={StatePath}", Constants.FoundryConfigurationStatePath);
            if (throwOnFailure)
            {
                throw;
            }
        }
    }

    private static FoundryConfigurationDocument SanitizeForPersistence(FoundryConfigurationDocument document)
    {
        DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(document.Autopilot, document.DomainJoin);
        return document with
        {
            General = SanitizeGeneralForPersistence(document.General),
            OperatingSystemSelection = OperatingSystemSelectionSettingsNormalizer.Normalize(document.OperatingSystemSelection),
            Customization = SanitizeCustomizationForPersistence(document.Customization),
            Autopilot = SanitizeAutopilotForPersistence(document.Autopilot)
        };
    }

    private static GeneralSettings SanitizeGeneralForPersistence(GeneralSettings settings)
    {
        return settings.Architecture == WinPeArchitecture.Arm64 && settings.UsbPartitionStyle == UsbPartitionStyle.Mbr
            ? settings with { UsbPartitionStyle = UsbPartitionStyle.Gpt }
            : settings;
    }

    private static AutopilotSettings SanitizeAutopilotForPersistence(AutopilotSettings settings)
    {
        // Keep persisted profiles deterministic because the selected profile is referenced by ID across sessions.
        AutopilotProfileSettings[] profiles = settings.Profiles
            .Where(profile =>
                !string.IsNullOrWhiteSpace(profile.Id) &&
                !string.IsNullOrWhiteSpace(profile.DisplayName) &&
                !string.IsNullOrWhiteSpace(profile.FolderName) &&
                !string.IsNullOrWhiteSpace(profile.Source) &&
                !string.IsNullOrWhiteSpace(profile.JsonContent))
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? defaultProfileId = profiles.Any(profile =>
            string.Equals(profile.Id, settings.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            ? settings.DefaultProfileId
            : profiles.FirstOrDefault()?.Id;

        return settings with
        {
            DefaultProfileId = defaultProfileId,
            Profiles = profiles,
            HardwareHashUpload = SanitizeHardwareHashUploadSettings(settings.HardwareHashUpload)
        };
    }

    private static AutopilotHardwareHashUploadSettings SanitizeHardwareHashUploadSettings(
        AutopilotHardwareHashUploadSettings? settings)
    {
        if (settings?.Tenant is null)
        {
            return new AutopilotHardwareHashUploadSettings();
        }

        string[] knownGroupTags = (settings.KnownGroupTags ?? [])
            .Select(groupTag => groupTag.Trim())
            .Where(groupTag => !string.IsNullOrWhiteSpace(groupTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(groupTag => groupTag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? defaultGroupTag = NormalizeOptional(settings.DefaultGroupTag);
        if (!string.IsNullOrWhiteSpace(defaultGroupTag) &&
            !knownGroupTags.Contains(defaultGroupTag, StringComparer.OrdinalIgnoreCase))
        {
            defaultGroupTag = null;
        }

        return settings with
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = NormalizeOptional(settings.Tenant.TenantId),
                ApplicationObjectId = NormalizeOptional(settings.Tenant.ApplicationObjectId),
                ClientId = NormalizeOptional(settings.Tenant.ClientId),
                ServicePrincipalObjectId = NormalizeOptional(settings.Tenant.ServicePrincipalObjectId)
            },
            ActiveCertificate = settings.ActiveCertificate is null
                ? null
                : settings.ActiveCertificate with
                {
                    KeyId = NormalizeOptional(settings.ActiveCertificate.KeyId),
                    Thumbprint = NormalizeOptional(settings.ActiveCertificate.Thumbprint)?.ToUpperInvariant(),
                    DisplayName = NormalizeOptional(settings.ActiveCertificate.DisplayName)
                },
            KnownGroupTags = knownGroupTags,
            DefaultGroupTag = defaultGroupTag
        };
    }

    private static CustomizationSettings SanitizeCustomizationForPersistence(CustomizationSettings settings)
    {
        MachineNamingSettings machineNaming = SanitizeMachineNaming(settings.MachineNaming);

        return settings with
        {
            MachineNaming = machineNaming,
            Oobe = SanitizeOobeForPersistence(settings.Oobe),
            AppxRemoval = SanitizeAppxRemovalForPersistence(settings.AppxRemoval),
            WindowsOptionalFeatures = WindowsOptionalFeatureSettingsNormalizer.Normalize(settings.WindowsOptionalFeatures),
            AiComponentRemoval = SanitizeAiComponentRemovalForPersistence(
                settings.AiComponentRemoval,
                settings.AppxRemoval)
        };
    }

    private static MachineNamingSettings SanitizeMachineNaming(MachineNamingSettings settings)
    {
        if (!settings.IsEnabled)
        {
            return new MachineNamingSettings();
        }

        return settings with
        {
            ManualInitialValue = settings.Mode == MachineNamingMode.Manual
                ? NormalizeOptional(ComputerNameRules.Normalize(settings.ManualInitialValue))
                : null,
            Components = settings.Mode == MachineNamingMode.Composed
                ? settings.Components.Select(component => component with
                {
                    StaticText = component.Type == MachineNameComponentType.StaticText
                        ? ComputerNameRules.Sanitize(component.StaticText)
                        : null
                }).ToArray()
                : []
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static OobeSettings SanitizeOobeForPersistence(OobeSettings settings)
    {
        return settings.IsEnabled
            ? settings
            : new OobeSettings();
    }

    private static AppxRemovalSettings SanitizeAppxRemovalForPersistence(AppxRemovalSettings settings)
    {
        string[] packageNames = NormalizeAppxRemovalPackageNames(settings.PackageNames);
        return settings.IsEnabled
            ? new AppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames = packageNames
            }
            : new AppxRemovalSettings();
    }

    private static AiComponentRemovalSettings SanitizeAiComponentRemovalForPersistence(
        AiComponentRemovalSettings settings,
        AppxRemovalSettings legacyAppxRemoval)
    {
        bool removeCopilot = settings.IsEnabled && settings.RemoveCopilot ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Copilot");
        bool removeAiHub = settings.IsEnabled && settings.RemoveAiHub ||
            HasLegacyAppxRemovalPackage(legacyAppxRemoval, "Microsoft.Windows.AIHub");
        var migratedSettings = new AiComponentRemovalSettings
        {
            IsEnabled = settings.IsEnabled || removeCopilot || removeAiHub,
            RemoveCopilot = removeCopilot,
            RemoveAiHub = removeAiHub,
            DisableRecall = settings.IsEnabled && settings.DisableRecall,
            DisableClickToDo = settings.IsEnabled && settings.DisableClickToDo,
            DisableAiServiceAutoStart = settings.IsEnabled && settings.DisableAiServiceAutoStart,
            DisableEdgeAi = settings.IsEnabled && settings.DisableEdgeAi,
            DisablePaintAi = settings.IsEnabled && settings.DisablePaintAi,
            DisableNotepadAi = settings.IsEnabled && settings.DisableNotepadAi
        };

        return migratedSettings.IsEnabled && migratedSettings.HasAnyAction()
            ? migratedSettings
            : new AiComponentRemovalSettings();
    }

    private static bool HasLegacyAppxRemovalPackage(AppxRemovalSettings settings, string packageName)
    {
        return settings.IsEnabled &&
            settings.PackageNames.Any(value => string.Equals(value.Trim(), packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeAppxRemovalPackageNames(IEnumerable<string> packageNames)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string packageName in packageNames)
        {
            string trimmed = packageName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                !AppxRemovalCatalog.ContainsPackageName(trimmed) ||
                !seen.Add(trimmed))
            {
                continue;
            }

            result.Add(trimmed);
        }

        return result.ToArray();
    }

    private NetworkMediaReadinessEvaluation EvaluateNetworkMediaReadiness()
    {
        return NetworkMediaReadinessEvaluator.Evaluate(Current.Network, MediaInputs);
    }

    private AutopilotProfileSettings? GetSelectedAutopilotProfile()
    {
        if (string.IsNullOrWhiteSpace(Current.Autopilot.DefaultProfileId))
        {
            return null;
        }

        return Current.Autopilot.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, Current.Autopilot.DefaultProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private void TryMoveInvalidState(string backupPath, Exception originalException)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(Constants.FoundryConfigurationStatePath, backupPath);
            logger.Warning(
                originalException,
                "Foundry configuration state was invalid and defaults were restored. StatePath={StatePath}, BackupPath={BackupPath}",
                Constants.FoundryConfigurationStatePath,
                backupPath);
        }
        catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
        {
            logger.Error(
                backupException,
                "Failed to back up invalid Foundry configuration state. StatePath={StatePath}, BackupPath={BackupPath}, OriginalError={OriginalError}",
                Constants.FoundryConfigurationStatePath,
                backupPath,
                originalException.Message);
            throw;
        }
    }
}

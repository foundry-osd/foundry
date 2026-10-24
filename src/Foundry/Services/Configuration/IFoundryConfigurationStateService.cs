// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Telemetry;

namespace Foundry.Services.Configuration;

/// <summary>
/// Owns the mutable Foundry configuration assembled by the Foundry UI.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is always safe to persist. Session passwords and the readable copies of referenced files are kept
/// outside the document and exposed as <see cref="MediaInputs"/>; <see cref="CaptureBuildSnapshotAsync"/> freezes both.
/// </remarks>
public interface IFoundryConfigurationStateService
{
    /// <summary>
    /// Occurs after the Foundry configuration changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Gets the current Foundry configuration document after removing values that must not be persisted.
    /// </summary>
    FoundryConfigurationDocument Current { get; }

    /// <summary>
    /// Gets whether a followed publication manages the configuration (spec 6.2): published sections are read-only, and every
    /// <c>Update*</c> method throws <see cref="InvalidOperationException"/> for a change outside the per-PC values
    /// (<see cref="Foundry.Core.Services.Sharing.ManagedConfigurationGuard"/>). <see cref="Replace"/> stays available to the follower.
    /// </summary>
    bool IsManaged { get; }

    /// <summary>Gets the display name of the reference PC that manages the configuration; null when not managed.</summary>
    string? ManagedBy { get; }

    /// <summary>Occurs when <see cref="IsManaged"/> or <see cref="ManagedBy"/> changes.</summary>
    event EventHandler? ManagedChanged;

    /// <summary>Marks the configuration managed by the reference named <paramref name="managedBy"/>.</summary>
    void SetManaged(string managedBy);

    /// <summary>Makes the configuration editable again; it stays as it is.</summary>
    void ClearManaged();

    /// <summary>
    /// Returns whether <paramref name="candidate"/> may replace <see cref="Current"/> through an <c>Update*</c> method: always when the
    /// configuration is not managed, otherwise only when it changes per-PC values alone. A view model that saves on its own (a
    /// normalization, a discovered default) checks it instead of having the update refused.
    /// </summary>
    bool IsChangeAllowed(FoundryConfigurationDocument candidate);

    /// <summary>
    /// Gets the configuration last written to disk, or loaded from it when no write succeeded yet; it differs from
    /// <see cref="Current"/> only after a failed save, and content cleanup keeps what either of them references.
    /// </summary>
    FoundryConfigurationDocument SavedConfiguration { get; }

    /// <summary>Persists a whole replacement configuration before restoring its local secrets and publishing it.</summary>
    /// <remarks>Increments <see cref="ReplacementVersion"/> before raising <see cref="StateChanged"/>.</remarks>
    void Replace(FoundryConfigurationDocument document, Action restoreSecrets);

    /// <summary>
    /// Gets a counter incremented by every <see cref="Replace"/>, so a long-running edit started before a replacement can
    /// detect it and avoid applying its result to the configuration that replaced the one it started from.
    /// </summary>
    long ReplacementVersion { get; }

    /// <summary>
    /// Gets the session inputs that validation and generation need beside <see cref="Current"/>: library paths of referenced
    /// authoring files and the passwords kept outside the document.
    /// </summary>
    MediaInputs MediaInputs { get; }

    /// <summary>Freezes current configuration and session secrets synchronously, then prepares private build dependencies off-thread.</summary>
    Task<BuildSnapshot> CaptureBuildSnapshotAsync(string privateRootDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the detailed network readiness evaluation used for media creation.
    /// </summary>
    NetworkMediaReadinessEvaluation NetworkMediaReadiness { get; }

    /// <summary>
    /// Gets a value indicating whether network configuration can be emitted for Connect.
    /// </summary>
    bool IsNetworkConfigurationReady { get; }

    /// <summary>
    /// Gets a value indicating whether Deploy configuration can be emitted.
    /// </summary>
    bool IsDeployConfigurationReady { get; }

    /// <summary>
    /// Gets a value indicating whether Connect provisioning files can be generated.
    /// </summary>
    bool IsConnectProvisioningReady { get; }

    /// <summary>
    /// Gets a value indicating whether required in-memory secrets are available.
    /// </summary>
    bool AreRequiredSecretsReady { get; }

    /// <summary>
    /// Gets a value indicating whether Autopilot provisioning is enabled.
    /// </summary>
    bool IsAutopilotEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the selected Autopilot profiles are valid for output.
    /// </summary>
    bool IsAutopilotConfigurationReady { get; }

    /// <summary>
    /// Gets whether custom answer-file sources and media protection are ready for generation.
    /// </summary>
    bool IsUnattendConfigurationReady { get; }

    /// <summary>
    /// Gets cached source inspections for the current catalog, without performing file I/O.
    /// </summary>
    IReadOnlyList<UnattendSourceValidation> UnattendSourceValidations { get; }

    /// <summary>
    /// Refreshes bounded source inspections off the UI thread and publishes current results through StateChanged.
    /// Late results from replaced catalogs are discarded; inaccessible sources never block the UI thread.
    /// </summary>
    Task RefreshUnattendSourcesAsync();

    /// <summary>
    /// Gets the detailed Autopilot readiness status for the selected provisioning mode.
    /// </summary>
    AutopilotConfigurationValidationResult AutopilotConfigurationValidation { get; }

    /// <summary>
    /// Gets the selected Autopilot provisioning mode.
    /// </summary>
    AutopilotProvisioningMode AutopilotProvisioningMode { get; }

    /// <summary>
    /// Gets the selected Autopilot profile display name when a single profile is selected.
    /// </summary>
    string? SelectedAutopilotProfileDisplayName { get; }

    /// <summary>
    /// Gets the selected Autopilot profile folder name when a single profile is selected.
    /// </summary>
    string? SelectedAutopilotProfileFolderName { get; }

    /// <summary>
    /// Replaces the general boot media authoring configuration section.
    /// </summary>
    /// <param name="settings">New general settings.</param>
    void UpdateGeneral(GeneralSettings settings);

    /// <summary>
    /// Replaces the network configuration section and stores required secrets in volatile state.
    /// </summary>
    /// <param name="settings">New network settings.</param>
    void UpdateNetwork(NetworkSettings settings);

    /// <summary>
    /// Replaces the OS catalog selection configuration section.
    /// </summary>
    /// <param name="settings">New OS catalog selection settings.</param>
    void UpdateOperatingSystemSelection(OperatingSystemSelectionSettings settings);

    /// <summary>
    /// Replaces the localization configuration section.
    /// </summary>
    /// <param name="settings">New localization settings.</param>
    void UpdateLocalization(LocalizationSettings settings);

    /// <summary>
    /// Replaces the customization configuration section.
    /// </summary>
    /// <param name="settings">New customization settings.</param>
    void UpdateCustomization(CustomizationSettings settings);

    /// <summary>
    /// Replaces the Autopilot configuration section.
    /// </summary>
    /// <param name="settings">New Autopilot settings.</param>
    void UpdateAutopilot(AutopilotSettings settings);

    /// <summary>Persists the Domain Join settings and erases the passwords of accounts they no longer store.</summary>
    void UpdateDomainJoin(DomainJoinSettings settings);

    /// <summary>Publishes one exclusive provisioning change after a successful candidate save.</summary>
    void UpdateProvisioningSelection(AutopilotSettings autopilot, DomainJoinSettings domainJoin);

    /// <summary>Gets domain metadata and automatic credential/protection readiness for the active mode.</summary>
    bool IsDomainJoinConfigurationReady { get; }

    /// <summary>
    /// Persists answer-file metadata and source references without storing XML content.
    /// </summary>
    /// <param name="settings">New answer-file settings.</param>
    void UpdateUnattend(UnattendSettings settings);

    /// <summary>Updates portable custom image references and deployment defaults.</summary>
    void UpdateCustomImages(CustomImagesSettings settings);

    /// <summary>Persists the ordered post-installation actions and portable package references.</summary>
    /// <param name="settings">The replacement action configuration.</param>
    /// <param name="requirePersistence">Requires a successful write before publishing the change, for destructive cache cleanup.</param>
    void UpdatePreOobe(PreOobeSettings settings, bool requirePersistence = false);

    /// <summary>Checks authoring validity and local availability before media generation.</summary>
    bool IsPostInstallationReady { get; }

    /// <summary>Invalidates overview readiness after local package content changes without changing the configuration.</summary>
    void RefreshPostInstallationReadiness();

    /// <summary>Gets basic custom image availability; full hash verification occurs under build leases.</summary>
    bool IsCustomImagesReady { get; }

    /// <summary>Invalidates overview readiness after local image content changes without changing the configuration.</summary>
    void RefreshCustomImageReadiness();

    /// <summary>Persists the custom driver set references, inclusion state and default set.</summary>
    /// <param name="settings">The replacement driver set configuration.</param>
    /// <param name="requirePersistence">Requires a successful write before publishing the change, for destructive cache cleanup.</param>
    void UpdateCustomDrivers(CustomDriverSetsSettings settings, bool requirePersistence = false);

    /// <summary>
    /// Checks that an enabled section includes a set and that no included set is known to be missing from the local library.
    /// Availability comes from the cache filled by <see cref="RefreshCustomDriversReadinessAsync"/>, so this never touches the
    /// filesystem; a set not inspected yet is not treated as missing. Full hash verification occurs when media preparation acquires the packages.
    /// </summary>
    bool IsCustomDriversReady { get; }

    /// <summary>Gets the cached library availability of a driver set package, or <see langword="null"/> when it was not inspected yet.</summary>
    bool? GetCustomDriverSetAvailability(string contentHash);

    /// <summary>Checks the library availability of every driver set of the configuration off the UI thread, then publishes the updated readiness.</summary>
    Task RefreshCustomDriversReadinessAsync();

    /// <summary>
    /// Checks one driver set package off the UI thread, then publishes the updated readiness. Called after an import so a
    /// set previously recorded as missing becomes available without a full refresh.
    /// </summary>
    Task InspectCustomDriverSetAsync(ContentPackageReference package);

    /// <summary>
    /// Replaces telemetry settings propagated into generated runtime configuration.
    /// </summary>
    /// <param name="settings">New telemetry settings.</param>
    void UpdateTelemetry(TelemetrySettings settings);

}

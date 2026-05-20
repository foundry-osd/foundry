using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// Evaluates the managed tenant registration state used by Autopilot hardware hash upload.
/// </summary>
public static class AutopilotTenantOnboardingEvaluator
{
    /// <summary>
    /// Computes the current onboarding status from Microsoft Graph state and persisted Foundry metadata.
    /// </summary>
    /// <param name="snapshot">Current tenant registration snapshot.</param>
    /// <returns>Evaluated onboarding status and resolved identifiers.</returns>
    public static AutopilotTenantOnboardingEvaluation Evaluate(AutopilotTenantOnboardingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        AutopilotGraphApplication? application = FindApplication(snapshot);
        if (application is null)
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(AutopilotTenantOnboardingStatus.AppRegistrationMissing);
        }

        if (string.IsNullOrWhiteSpace(snapshot.PersistedApplicationObjectId) &&
            string.Equals(application.DisplayName, snapshot.ManagedAppDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.AdoptionRequired,
                application);
        }

        if (!AutopilotGraphPermissionCatalog.HasRequiredWinPeApplicationPermissions(application.RequiredPermissionValues))
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.PermissionMissing,
                application);
        }

        if (snapshot.ServicePrincipal is not { IsEnabled: true })
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.ServicePrincipalUnavailable,
                application);
        }

        if (!AutopilotGraphPermissionCatalog.HasRequiredWinPeApplicationPermissions(snapshot.ServicePrincipal.ConsentedPermissionValues))
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.ConsentMissing,
                application,
                snapshot.ServicePrincipal);
        }

        AutopilotCertificateMetadata? activeCertificate = snapshot.ActiveCertificate;
        if (activeCertificate is null)
        {
            int foundryCredentialCount = snapshot.KeyCredentials.Count(credential =>
                string.Equals(credential.DisplayName, snapshot.ManagedAppDisplayName, StringComparison.OrdinalIgnoreCase));
            return foundryCredentialCount > 1
                ? AutopilotTenantOnboardingEvaluation.FromStatus(
                    AutopilotTenantOnboardingStatus.MultipleFoundryCertificatesNeedSelection,
                    application,
                    snapshot.ServicePrincipal)
                : AutopilotTenantOnboardingEvaluation.FromStatus(
                    AutopilotTenantOnboardingStatus.ActiveCertificateMissing,
                    application,
                    snapshot.ServicePrincipal);
        }

        AutopilotGraphKeyCredential? graphCredential = snapshot.KeyCredentials.FirstOrDefault(credential =>
            string.Equals(credential.KeyId, activeCertificate.KeyId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeThumbprint(credential.Thumbprint), NormalizeThumbprint(activeCertificate.Thumbprint), StringComparison.Ordinal));
        if (graphCredential is null)
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.ActiveCertificateNotFound,
                application,
                snapshot.ServicePrincipal);
        }

        if (graphCredential.ExpiresOnUtc <= snapshot.CurrentTimeUtc)
        {
            return AutopilotTenantOnboardingEvaluation.FromStatus(
                AutopilotTenantOnboardingStatus.ActiveCertificateExpired,
                application,
                snapshot.ServicePrincipal);
        }

        return AutopilotTenantOnboardingEvaluation.FromStatus(
            AutopilotTenantOnboardingStatus.Ready,
            application,
            snapshot.ServicePrincipal,
            graphCredential);
    }

    private static AutopilotGraphApplication? FindApplication(AutopilotTenantOnboardingSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.PersistedApplicationObjectId))
        {
            AutopilotGraphApplication? persisted = snapshot.Applications.FirstOrDefault(application =>
                string.Equals(application.ObjectId, snapshot.PersistedApplicationObjectId, StringComparison.OrdinalIgnoreCase));
            if (persisted is not null)
            {
                return persisted;
            }
        }

        return snapshot.Applications.FirstOrDefault(application =>
            string.Equals(application.DisplayName, snapshot.ManagedAppDisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeThumbprint(string? thumbprint)
    {
        string? normalized = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToUpperInvariant();
    }
}

/// <summary>
/// Represents the Microsoft Graph and persisted Foundry state required to evaluate tenant onboarding.
/// </summary>
public sealed record AutopilotTenantOnboardingSnapshot
{
    /// <summary>Gets the connected tenant ID.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the persisted managed app object ID, when Foundry already owns one.</summary>
    public string? PersistedApplicationObjectId { get; init; }

    /// <summary>Gets the display name expected for the managed Foundry app registration.</summary>
    public required string ManagedAppDisplayName { get; init; }

    /// <summary>Gets candidate applications discovered in Microsoft Graph.</summary>
    public IReadOnlyList<AutopilotGraphApplication> Applications { get; init; } = [];

    /// <summary>Gets the managed app service principal, when it exists.</summary>
    public AutopilotGraphServicePrincipal? ServicePrincipal { get; init; }

    /// <summary>Gets the active certificate metadata persisted by Foundry.</summary>
    public AutopilotCertificateMetadata? ActiveCertificate { get; init; }

    /// <summary>Gets certificate credentials currently present on the app registration.</summary>
    public IReadOnlyList<AutopilotGraphKeyCredential> KeyCredentials { get; init; } = [];

    /// <summary>Gets the evaluation clock used for certificate expiration checks.</summary>
    public DateTimeOffset CurrentTimeUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of evaluating the managed Autopilot app registration onboarding state.
/// </summary>
public sealed record AutopilotTenantOnboardingEvaluation
{
    /// <summary>Gets the evaluated onboarding status.</summary>
    public AutopilotTenantOnboardingStatus Status { get; init; }

    /// <summary>Gets the selected application object ID, when available.</summary>
    public string? ApplicationObjectId { get; init; }

    /// <summary>Gets the selected application client ID, when available.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the selected service principal object ID, when available.</summary>
    public string? ServicePrincipalObjectId { get; init; }

    /// <summary>Gets the active app credential found in Microsoft Graph, when available.</summary>
    public AutopilotGraphKeyCredential? ActiveCertificateCredential { get; init; }

    /// <summary>
    /// Creates an evaluation result for the supplied status and resolved Graph objects.
    /// </summary>
    public static AutopilotTenantOnboardingEvaluation FromStatus(
        AutopilotTenantOnboardingStatus status,
        AutopilotGraphApplication? application = null,
        AutopilotGraphServicePrincipal? servicePrincipal = null,
        AutopilotGraphKeyCredential? activeCertificateCredential = null)
    {
        return new AutopilotTenantOnboardingEvaluation
        {
            Status = status,
            ApplicationObjectId = application?.ObjectId,
            ClientId = application?.ClientId,
            ServicePrincipalObjectId = servicePrincipal?.ObjectId,
            ActiveCertificateCredential = activeCertificateCredential
        };
    }
}

/// <summary>
/// Autopilot hardware hash tenant onboarding status.
/// </summary>
public enum AutopilotTenantOnboardingStatus
{
    /// <summary>The managed app registration is ready for hardware hash upload media generation.</summary>
    Ready,

    /// <summary>The managed app registration does not exist.</summary>
    AppRegistrationMissing,

    /// <summary>An app with the managed display name exists but is not yet adopted by persisted Foundry metadata.</summary>
    AdoptionRequired,

    /// <summary>The managed app registration is missing required Graph application permissions.</summary>
    PermissionMissing,

    /// <summary>The managed service principal is missing admin consent for required Graph permissions.</summary>
    ConsentMissing,

    /// <summary>The managed service principal is missing or disabled.</summary>
    ServicePrincipalUnavailable,

    /// <summary>No active certificate is selected in persisted Foundry metadata.</summary>
    ActiveCertificateMissing,

    /// <summary>The selected active certificate was not found in the app registration credentials.</summary>
    ActiveCertificateNotFound,

    /// <summary>The selected active certificate is expired.</summary>
    ActiveCertificateExpired,

    /// <summary>Multiple Foundry-looking certificates exist and no persisted active certificate resolves ownership.</summary>
    MultipleFoundryCertificatesNeedSelection
}

/// <summary>
/// Minimal Microsoft Graph application state required for Autopilot onboarding evaluation.
/// </summary>
public sealed record AutopilotGraphApplication(
    string ObjectId,
    string ClientId,
    string DisplayName,
    IReadOnlySet<string> RequiredPermissionValues);

/// <summary>
/// Minimal Microsoft Graph service principal state required for Autopilot onboarding evaluation.
/// </summary>
public sealed record AutopilotGraphServicePrincipal(
    string ObjectId,
    bool IsEnabled,
    IReadOnlySet<string> ConsentedPermissionValues);

/// <summary>
/// Minimal Microsoft Graph certificate credential state required for Autopilot onboarding evaluation.
/// </summary>
public sealed record AutopilotGraphKeyCredential(
    string KeyId,
    string DisplayName,
    string Thumbprint,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset ExpiresOnUtc);

/// <summary>
/// Central catalog of Graph permissions required by the WinPE app-only upload path.
/// </summary>
public static class AutopilotGraphPermissionCatalog
{
    /// <summary>
    /// Graph application permission required to import and poll Windows Autopilot device identities.
    /// </summary>
    public const string DeviceManagementServiceConfigReadWriteAll = "DeviceManagementServiceConfig.ReadWrite.All";

    /// <summary>
    /// Gets the required Graph application permissions for WinPE app-only upload.
    /// </summary>
    public static readonly IReadOnlySet<string> RequiredWinPeApplicationPermissionValues =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DeviceManagementServiceConfigReadWriteAll
        };

    /// <summary>
    /// Determines whether the supplied permission values include every permission required by WinPE upload.
    /// </summary>
    /// <param name="permissionValues">Permission values to evaluate.</param>
    /// <returns><see langword="true"/> when all required permissions are present.</returns>
    public static bool HasRequiredWinPeApplicationPermissions(IReadOnlySet<string> permissionValues)
    {
        ArgumentNullException.ThrowIfNull(permissionValues);
        return RequiredWinPeApplicationPermissionValues.All(permissionValues.Contains);
    }
}

/// <summary>
/// Provides pure helpers for safe app registration certificate credential collection updates.
/// </summary>
public static class AutopilotAppRegistrationCertificateCollection
{
    /// <summary>
    /// Adds or replaces one certificate credential without pruning unrelated credentials.
    /// </summary>
    public static IReadOnlyList<AutopilotGraphKeyCredential> AddCertificate(
        IReadOnlyList<AutopilotGraphKeyCredential> currentCredentials,
        AutopilotGraphKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(currentCredentials);
        ArgumentNullException.ThrowIfNull(credential);
        return currentCredentials
            .Where(existing => !string.Equals(existing.KeyId, credential.KeyId, StringComparison.OrdinalIgnoreCase))
            .Concat([credential])
            .ToArray();
    }

    /// <summary>
    /// Replaces the persisted active certificate credential while preserving unrelated credentials.
    /// </summary>
    public static IReadOnlyList<AutopilotGraphKeyCredential> ReplaceActiveCertificate(
        IReadOnlyList<AutopilotGraphKeyCredential> currentCredentials,
        AutopilotGraphKeyCredential credential,
        string? activeKeyIdToReplace)
    {
        ArgumentNullException.ThrowIfNull(currentCredentials);
        ArgumentNullException.ThrowIfNull(credential);
        return currentCredentials
            .Where(existing =>
                !string.Equals(existing.KeyId, credential.KeyId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(activeKeyIdToReplace) ||
                 !string.Equals(existing.KeyId, activeKeyIdToReplace, StringComparison.OrdinalIgnoreCase)))
            .Concat([credential])
            .ToArray();
    }

    /// <summary>
    /// Removes only the persisted active certificate credential.
    /// </summary>
    public static IReadOnlyList<AutopilotGraphKeyCredential> RetireActiveCertificate(
        IReadOnlyList<AutopilotGraphKeyCredential> currentCredentials,
        string activeKeyId)
    {
        ArgumentNullException.ThrowIfNull(currentCredentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);
        return currentCredentials
            .Where(credential => !string.Equals(credential.KeyId, activeKeyId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

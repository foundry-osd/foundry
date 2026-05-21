using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotTenantOnboardingEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_UsesPersistedApplicationObjectIdBeforeDisplayName()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            applications:
            [
                CreateApplication("persisted-object-id", "client-id", "Different display name"),
                CreateApplication("same-name-object-id", "other-client-id", AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName)
            ]));

        Assert.Equal(AutopilotTenantOnboardingStatus.Ready, result.Status);
        Assert.Equal("persisted-object-id", result.ApplicationObjectId);
        Assert.Equal("client-id", result.ClientId);
    }

    [Fact]
    public void Evaluate_WhenSameDisplayNameExistsWithoutPersistedObjectId_ReturnsAdoptionRequired()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            persistedApplicationObjectId: null,
            applications:
            [
                CreateApplication("existing-object-id", "client-id", AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName)
            ]));

        Assert.Equal(AutopilotTenantOnboardingStatus.AdoptionRequired, result.Status);
    }

    [Fact]
    public void Evaluate_WhenRequiredPermissionMissing_ReturnsPermissionMissing()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            applications:
            [
                CreateApplication(
                    "persisted-object-id",
                    "client-id",
                    AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DeviceManagementServiceConfig.Read.All" })
            ]));

        Assert.Equal(AutopilotTenantOnboardingStatus.PermissionMissing, result.Status);
    }

    [Fact]
    public void Evaluate_WhenAdminConsentMissing_ReturnsConsentMissing()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            servicePrincipal: CreateServicePrincipal(consentedPermissionValues: new HashSet<string>(StringComparer.OrdinalIgnoreCase))));

        Assert.Equal(AutopilotTenantOnboardingStatus.ConsentMissing, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_WhenServicePrincipalIsMissingOrDisabled_ReturnsServicePrincipalUnavailable(bool servicePrincipalExists)
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            servicePrincipal: servicePrincipalExists ? CreateServicePrincipal(isEnabled: false) : null,
            useDefaultServicePrincipal: servicePrincipalExists));

        Assert.Equal(AutopilotTenantOnboardingStatus.ServicePrincipalUnavailable, result.Status);
    }

    [Fact]
    public void Evaluate_WhenActiveCertificateIsMissingFromGraph_ReturnsActiveCertificateNotFound()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            keyCredentials:
            [
                CreateKeyCredential("other-key-id", "OTHER", Now.AddMonths(12))
            ]));

        Assert.Equal(AutopilotTenantOnboardingStatus.ActiveCertificateNotFound, result.Status);
    }

    [Fact]
    public void Evaluate_WhenFoundryCredentialsExistWithoutPersistedActiveCertificate_ReturnsReady()
    {
        AutopilotTenantOnboardingEvaluation result = AutopilotTenantOnboardingEvaluator.Evaluate(CreateSnapshot(
            activeCertificate: null,
            useDefaultActiveCertificate: false,
            keyCredentials:
            [
                CreateKeyCredential("key-1", "AAA", Now.AddMonths(12)),
                CreateKeyCredential("key-2", "BBB", Now.AddMonths(12))
            ]));

        Assert.Equal(AutopilotTenantOnboardingStatus.Ready, result.Status);
    }

    [Fact]
    public void AddCertificateCredential_PreservesExistingCredentials()
    {
        AutopilotGraphKeyCredential existing = CreateKeyCredential("existing-key-id", "AAA", Now.AddMonths(12));
        AutopilotGraphKeyCredential added = CreateKeyCredential("new-key-id", "BBB", Now.AddMonths(24));

        IReadOnlyList<AutopilotGraphKeyCredential> result =
            AutopilotAppRegistrationCertificateCollection.AddCertificate([existing], added);

        Assert.Equal(["existing-key-id", "new-key-id"], result.Select(credential => credential.KeyId));
    }

    [Fact]
    public void RetireActiveCertificate_RemovesOnlyPersistedActiveKeyId()
    {
        AutopilotGraphKeyCredential active = CreateKeyCredential("active-key-id", "AAA", Now.AddMonths(12));
        AutopilotGraphKeyCredential other = CreateKeyCredential("other-key-id", "BBB", Now.AddMonths(24));

        IReadOnlyList<AutopilotGraphKeyCredential> result =
            AutopilotAppRegistrationCertificateCollection.RetireActiveCertificate([active, other], "active-key-id");

        Assert.Single(result);
        Assert.Equal("other-key-id", result[0].KeyId);
    }

    private static AutopilotTenantOnboardingSnapshot CreateSnapshot(
        string? persistedApplicationObjectId = "persisted-object-id",
        IReadOnlyList<AutopilotGraphApplication>? applications = null,
        AutopilotGraphServicePrincipal? servicePrincipal = null,
        AutopilotCertificateMetadata? activeCertificate = null,
        IReadOnlyList<AutopilotGraphKeyCredential>? keyCredentials = null,
        bool useDefaultServicePrincipal = true,
        bool useDefaultActiveCertificate = true)
    {
        return new AutopilotTenantOnboardingSnapshot
        {
            TenantId = "tenant-id",
            PersistedApplicationObjectId = persistedApplicationObjectId,
            ManagedAppDisplayName = AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
            Applications = applications ?? [CreateApplication("persisted-object-id", "client-id", AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName)],
            ServicePrincipal = servicePrincipal ?? (useDefaultServicePrincipal ? CreateServicePrincipal() : null),
            ActiveCertificate = activeCertificate ?? (useDefaultActiveCertificate ? new AutopilotCertificateMetadata
            {
                KeyId = "active-key-id",
                Thumbprint = "ABCDEF123456",
                ExpiresOnUtc = Now.AddMonths(12)
            } : null),
            KeyCredentials = keyCredentials ?? [CreateKeyCredential("active-key-id", "ABCDEF123456", Now.AddMonths(12))],
            CurrentTimeUtc = Now
        };
    }

    private static AutopilotGraphApplication CreateApplication(
        string objectId,
        string clientId,
        string displayName,
        IReadOnlySet<string>? requiredPermissionValues = null)
    {
        return new AutopilotGraphApplication(
            objectId,
            clientId,
            displayName,
            requiredPermissionValues ?? AutopilotGraphPermissionCatalog.RequiredWinPeApplicationPermissionValues);
    }

    private static AutopilotGraphServicePrincipal CreateServicePrincipal(
        bool isEnabled = true,
        IReadOnlySet<string>? consentedPermissionValues = null)
    {
        return new AutopilotGraphServicePrincipal(
            "service-principal-object-id",
            isEnabled,
            consentedPermissionValues ?? AutopilotGraphPermissionCatalog.RequiredWinPeApplicationPermissionValues);
    }

    private static AutopilotGraphKeyCredential CreateKeyCredential(
        string keyId,
        string thumbprint,
        DateTimeOffset expiresOnUtc)
    {
        return new AutopilotGraphKeyCredential(
            keyId,
            AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
            thumbprint,
            Now.AddDays(-1),
            expiresOnUtc);
    }
}

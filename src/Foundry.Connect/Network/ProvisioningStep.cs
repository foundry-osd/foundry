// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Network;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Applies the media network provisioning once per process: the wired 802.1X profile with its certificate and a
/// reconnect of every linked Ethernet interface (T8), then the provisioned Wi-Fi profile on every present WLAN
/// interface (T1).
/// </summary>
/// <remarks>
/// A secret that cannot be read makes only its feature unavailable (D9). PFX passwords are cleared right after the
/// certificate import and the Wi-Fi passphrase right after the profile document is built (T10, T15, L2-6). The wired
/// and Wi-Fi parts are isolated: an unexpected error in one never stops the other. Roaming is not captured here (D5).
/// Only Debug details are logged; the coordinator logs the operation and its failures. No SSID, profile name or
/// profile XML is ever logged.
/// </remarks>
public sealed class ProvisioningStep : IProvisioningStep
{
    private readonly object _gate = new();
    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly string? _configurationPath;
    private readonly ConnectRuntimeSecrets _secrets;
    private readonly ICertificateInstaller _certificates;
    private readonly IWiredDot1xClient _wired;
    private readonly IWlanClient _wlan;
    private readonly INetworkAdapterSnapshotProvider _adapters;
    private readonly ILogger<ProvisioningStep> _logger;
    private Task<ProvisioningOutcome>? _run;

    /// <summary>
    /// Initializes the step; relative asset paths resolve against the loaded configuration file.
    /// </summary>
    public ProvisioningStep(
        FoundryConnectConfigurationDocument configuration,
        IConnectConfigurationService configurationService,
        ConnectRuntimeSecrets secrets,
        ICertificateInstaller certificates,
        IWiredDot1xClient wired,
        IWlanClient wlan,
        INetworkAdapterSnapshotProvider adapters,
        ILogger<ProvisioningStep> logger)
        : this(
            configuration,
            (configurationService ?? throw new ArgumentNullException(nameof(configurationService))).ConfigurationPath,
            secrets,
            certificates,
            wired,
            wlan,
            adapters,
            logger)
    {
    }

    /// <summary>
    /// Initializes the step with an explicit configuration file path that anchors relative asset paths.
    /// </summary>
    internal ProvisioningStep(
        FoundryConnectConfigurationDocument configuration,
        string? configurationPath,
        ConnectRuntimeSecrets secrets,
        ICertificateInstaller certificates,
        IWiredDot1xClient wired,
        IWlanClient wlan,
        INetworkAdapterSnapshotProvider adapters,
        ILogger<ProvisioningStep> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configurationPath = configurationPath;
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _wired = wired ?? throw new ArgumentNullException(nameof(wired));
        _wlan = wlan ?? throw new ArgumentNullException(nameof(wlan));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private enum CertificateStepResult
    {
        Completed,
        SecretUnavailable
    }

    /// <inheritdoc />
    public Task<ProvisioningOutcome> RunAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // A cancelled first run is not kept; a run only faults when it was cancelled.
            if (_run is null || (_run.IsCompleted && !_run.IsCompletedSuccessfully))
            {
                // Certificate import and file reads block, so the work starts on the thread pool, outside the lock.
                _run = Task.Run(() => RunCoreAsync(cancellationToken), CancellationToken.None);
            }

            return _run;
        }
    }

    private async Task<ProvisioningOutcome> RunCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<NetworkFailureCode> failures = [];

        Dot1xState wired = await ProvisionWiredAsync(failures, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        (ProvisionedWifiState wifi, WifiProfileDocument? profile) = ProvisionWifi(failures, cancellationToken);

        return new ProvisioningOutcome(wired, wifi, profile, failures.ToArray());
    }

    private async Task<Dot1xState> ProvisionWiredAsync(List<NetworkFailureCode> failures, CancellationToken cancellationToken)
    {
        ConnectDot1xSettings settings = _configuration.Dot1x;
        if (!settings.IsEnabled)
        {
            return Dot1xState.NotConfigured;
        }

        try
        {
            return await ApplyWiredAsync(settings, failures, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedFailure(exception, "wired");
            failures.Add(NetworkFailureCode.Unexpected);
            return new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed);
        }
        finally
        {
            _secrets.WiredCertificatePfxPassword.Clear();
        }
    }

    private async Task<Dot1xState> ApplyWiredAsync(ConnectDot1xSettings settings, List<NetworkFailureCode> failures, CancellationToken cancellationToken)
    {
        string? templatePath = ResolveAssetPath(settings.ProfileTemplatePath);
        if (templatePath is null || !File.Exists(templatePath))
        {
            failures.Add(NetworkFailureCode.WiredProfileTemplateMissing);
            return new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileTemplateMissing);
        }

        CertificateStepResult certificate = InstallCertificate(
            settings.CertificatePath,
            settings.CertificatePfxPasswordSecret,
            _secrets.WiredCertificatePfxPassword,
            NetworkFailureCode.WiredCertificateImportFailed,
            failures);
        if (certificate == CertificateStepResult.SecretUnavailable)
        {
            failures.Add(NetworkFailureCode.WiredSecretUnavailable);
            return new Dot1xState(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable);
        }

        WiredCommandResult added = await _wired.AddProfileAsync(templatePath, cancellationToken).ConfigureAwait(false);
        if (!added.Succeeded)
        {
            _logger.LogDebug(
                "Wired 802.1X profile was not added. ProcessStatus={ProcessStatus}, ExitCode={ExitCode}",
                added.Status,
                added.ExitCode);
            failures.Add(NetworkFailureCode.WiredProfileImportFailed);
            return new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed);
        }

        await ReconnectEthernetAsync(failures, cancellationToken).ConfigureAwait(false);
        return new Dot1xState(Dot1xAuthenticationStatus.ProfileApplied);
    }

    private async Task ReconnectEthernetAsync(List<NetworkFailureCode> failures, CancellationToken cancellationToken)
    {
        string[] interfaceNames = _adapters.GetAdapters()
            .Where(static adapter => EthernetStateReader.IsEthernet(adapter.InterfaceType)
                && adapter.OperationalStatus == OperationalStatus.Up
                && !string.IsNullOrWhiteSpace(adapter.Name))
            .Select(static adapter => adapter.Name)
            .ToArray();

        int failedCount = 0;
        foreach (string interfaceName in interfaceNames)
        {
            WiredCommandResult result = await _wired.ReconnectAsync(interfaceName, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                failedCount++;
            }
        }

        _logger.LogDebug(
            "Wired 802.1X reconnect requested. InterfaceCount={InterfaceCount}, FailedCount={FailedCount}",
            interfaceNames.Length,
            failedCount);
        if (failedCount > 0)
        {
            failures.Add(NetworkFailureCode.WiredReconnectFailed);
        }
    }

    private (ProvisionedWifiState State, WifiProfileDocument? Profile) ProvisionWifi(List<NetworkFailureCode> failures, CancellationToken cancellationToken)
    {
        ConnectWifiSettings settings = _configuration.Wifi;
        if (!_configuration.Capabilities.WifiProvisioned || !settings.IsEnabled)
        {
            return (ProvisionedWifiState.NotConfigured, null);
        }

        try
        {
            (ProvisionedWifiState state, WifiProfileDocument? profile) = settings.HasEnterpriseProfile
                ? ReadEnterpriseProfile(settings, failures)
                : BuildPersonalProfile(settings, failures);
            if (profile is null)
            {
                return (state, null);
            }

            SetProfileOnPresentInterfaces(profile, failures);
            return (state, profile);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedFailure(exception, "wifi");
            failures.Add(NetworkFailureCode.Unexpected);
            return (new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.InvalidConfiguration, IsEnterprise: settings.HasEnterpriseProfile), null);
        }
        finally
        {
            _secrets.WifiPassphrase.Clear();
            _secrets.WifiCertificatePfxPassword.Clear();
        }
    }

    private (ProvisionedWifiState State, WifiProfileDocument? Profile) ReadEnterpriseProfile(ConnectWifiSettings settings, List<NetworkFailureCode> failures)
    {
        string? templatePath = ResolveAssetPath(settings.EnterpriseProfileTemplatePath);
        if (templatePath is null || !File.Exists(templatePath))
        {
            failures.Add(NetworkFailureCode.ProvisionedProfileUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.ProfileTemplateMissing, isEnterprise: true), null);
        }

        CertificateStepResult certificate = InstallCertificate(
            settings.CertificatePath,
            settings.CertificatePfxPasswordSecret,
            _secrets.WifiCertificatePfxPassword,
            NetworkFailureCode.WifiCertificateImportFailed,
            failures);
        if (certificate == CertificateStepResult.SecretUnavailable)
        {
            failures.Add(NetworkFailureCode.WifiSecretUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.SecretUnavailable, isEnterprise: true), null);
        }

        // A template without a readable name or SSID fails closed instead of guessing an identity.
        WifiProfileDocument? profile = WifiProfileDocument.FromXml(File.ReadAllText(templatePath));
        if (profile is null)
        {
            failures.Add(NetworkFailureCode.ProvisionedProfileUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.InvalidConfiguration, isEnterprise: true), null);
        }

        return (Ready(profile, isEnterprise: true), profile);
    }

    private (ProvisionedWifiState State, WifiProfileDocument? Profile) BuildPersonalProfile(ConnectWifiSettings settings, List<NetworkFailureCode> failures)
    {
        // L2-21: the configured SSID keeps today's trim; the passphrase is never trimmed (T9).
        string? ssidText = settings.Ssid?.Trim();
        WifiSsid? ssid = null;
        WifiProfileSecurity? security = WifiSecurityClassifier.FromConfiguredSecurityType(settings.SecurityType);
        if (string.IsNullOrEmpty(ssidText) || !WifiSsid.TryFromBytes(Encoding.UTF8.GetBytes(ssidText), out ssid) || security is null)
        {
            failures.Add(NetworkFailureCode.ProvisionedProfileUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.InvalidConfiguration, isEnterprise: false, ssid), null);
        }

        ConnectSecret passphrase = _secrets.WifiPassphrase;
        bool isPersonal = security is WifiProfileSecurity.Wpa2Personal or WifiProfileSecurity.Wpa3Personal or WifiProfileSecurity.Wpa2Wpa3Personal;
        if (isPersonal && passphrase.Status == ConnectSecretStatus.Unavailable)
        {
            failures.Add(NetworkFailureCode.WifiSecretUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.SecretUnavailable, isEnterprise: false, ssid), null);
        }

        WifiProfileBuildResult build;
        try
        {
            build = WifiProfileFactory.Build(
                ssid,
                security.Value,
                passphrase.Status == ConnectSecretStatus.Available ? passphrase.Value : ReadOnlySpan<char>.Empty);
        }
        finally
        {
            passphrase.Clear();
        }

        if (build.Profile is null)
        {
            _logger.LogDebug("Provisioned Wi-Fi passphrase was rejected. ProfileError={ProfileError}", build.Error);
            failures.Add(NetworkFailureCode.InvalidPassphrase);
            return (Unavailable(ProvisionedWifiUnavailableReason.InvalidPassphrase, isEnterprise: false, ssid), null);
        }

        return (Ready(build.Profile, isEnterprise: false), build.Profile);
    }

    private void SetProfileOnPresentInterfaces(WifiProfileDocument profile, List<NetworkFailureCode> failures)
    {
        WlanSnapshot snapshot = _wlan.GetSnapshot(requestScan: false);
        WlanOperationResult? lastFailure = null;
        int rejectedCount = 0;
        foreach (WlanInterfaceSnapshot wlanInterface in snapshot.Interfaces)
        {
            WlanOperationResult result = _wlan.SetProfile(wlanInterface.InterfaceId, profile.Xml);
            if (!result.IsSuccess)
            {
                rejectedCount++;
                lastFailure = result;
            }
        }

        _logger.LogDebug(
            "Provisioned Wi-Fi profile set on present WLAN interfaces. WifiAvailability={WifiAvailability}, InterfaceCount={InterfaceCount}, RejectedCount={RejectedCount}, WlanStatus={WlanStatus}, Win32Error={Win32Error}, WlanReasonCode={WlanReasonCode}",
            snapshot.Availability,
            snapshot.Interfaces.Count,
            rejectedCount,
            lastFailure?.Status,
            lastFailure?.Win32Error,
            lastFailure?.ReasonCode);

        // The coordinator sets the profile again before each provisioned connect when the interface lacks it.
        if (rejectedCount > 0)
        {
            failures.Add(NetworkFailureCode.WifiProfileRejected);
        }
    }

    /// <summary>
    /// Imports the configured certificate when its file exists, then clears its PFX password.
    /// </summary>
    /// <returns>
    /// <see cref="CertificateStepResult.SecretUnavailable"/> when a PFX file needs a password the media carries but that
    /// could not be read; the caller then stops its part. An import failure only adds <paramref name="importFailure"/>.
    /// </returns>
    private CertificateStepResult InstallCertificate(
        string? configuredPath,
        SecretEnvelope? passwordEnvelope,
        ConnectSecret password,
        NetworkFailureCode importFailure,
        List<NetworkFailureCode> failures)
    {
        try
        {
            string? certificatePath = ResolveAssetPath(configuredPath);
            if (certificatePath is null || !File.Exists(certificatePath))
            {
                return CertificateStepResult.Completed;
            }

            if (NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath) && passwordEnvelope is not null && password.Status == ConnectSecretStatus.Unavailable)
            {
                return CertificateStepResult.SecretUnavailable;
            }

            CertificateInstallResult result = _certificates.Install(
                certificatePath,
                password.Status == ConnectSecretStatus.Available ? password.Value : ReadOnlySpan<char>.Empty);
            _logger.LogDebug(
                "Provisioned certificate processed. CertificateKind={CertificateKind}, CertificateInstallStatus={CertificateInstallStatus}",
                CertificateInstaller.GetCertificateKind(certificatePath),
                result.Status);
            if (!result.IsSuccess)
            {
                failures.Add(importFailure);
            }

            return CertificateStepResult.Completed;
        }
        finally
        {
            password.Clear();
        }
    }

    private string? ResolveAssetPath(string? value) => ProvisionedWifiProfileResolver.ResolveAssetPath(value, _configurationPath);

    private void LogUnexpectedFailure(Exception exception, string part)
    {
        NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.Describe(NetworkFailureCode.Unexpected);
        _logger.LogWarning(
            exception,
            "Network provisioning part failed unexpectedly. ProvisioningPart={ProvisioningPart}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}",
            part,
            descriptor.Kind,
            descriptor.Reason,
            descriptor.Code);
    }

    private static ProvisionedWifiState Ready(WifiProfileDocument profile, bool isEnterprise) =>
        new(ProvisionedWifiStatus.Ready, ProvisionedWifiUnavailableReason.None, profile.Ssid, profile.Name, isEnterprise, profile.IsHidden);

    private static ProvisionedWifiState Unavailable(ProvisionedWifiUnavailableReason reason, bool isEnterprise, WifiSsid? ssid = null) =>
        new(ProvisionedWifiStatus.Unavailable, reason, ssid, IsEnterprise: isEnterprise);
}

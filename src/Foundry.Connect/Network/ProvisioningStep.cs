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
/// Applies the media network provisioning once per process: the wired 802.1X profile with its certificates and a
/// reconnect of every linked Ethernet interface (T8), then the provisioned Wi-Fi profile on every present WLAN
/// interface (T1).
/// </summary>
/// <remarks>
/// Each part installs every trusted root of its <see cref="ConnectCertificateSet"/> and its client PFX, read from a plain
/// file or, when staged encrypted, from the decrypted content (D8, L4-5). A secret that cannot be read makes only its
/// feature unavailable (D9). A part whose secrets are still locked by the deployment password is skipped: wired 802.1X
/// becomes unavailable for the session, the provisioned Wi-Fi stays <see cref="ProvisionedWifiStatus.Locked"/> until
/// <see cref="RefreshWifiAsync"/> runs after the unlock (L4-10). PFX passwords and contents are cleared right after the
/// certificate import, the Wi-Fi passphrase right after the profile document is built and a protected template right
/// after it is decoded (T10, T15, L2-6). The wired and Wi-Fi parts are isolated: an unexpected error in one never stops
/// the other. Roaming is not captured here (D5). Only Debug details and the wired skip are logged; the coordinator logs
/// the operation and its failures. No SSID, profile name, file name or profile XML is ever logged.
/// </remarks>
public sealed class ProvisioningStep : IProvisioningStep
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
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

    /// <inheritdoc />
    public async Task<ProvisioningOutcome> RefreshWifiAsync(CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken).ConfigureAwait(false);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read again under the refresh gate: a refresh this call waited behind may have replaced the cached outcome.
            ProvisioningOutcome cached = await RunAsync(cancellationToken).ConfigureAwait(false);
            ProvisioningOutcome refreshed = await Task.Run(() => RefreshCore(cached, cancellationToken), CancellationToken.None).ConfigureAwait(false);
            if (!ReferenceEquals(refreshed, cached))
            {
                lock (_gate)
                {
                    _run = Task.FromResult(refreshed);
                }
            }

            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
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

    private ProvisioningOutcome RefreshCore(ProvisioningOutcome cached, CancellationToken cancellationToken)
    {
        // The wired part never runs again this session, so a wired secret a later unlock made readable is never used (L4-10).
        ClearWiredSecrets();
        if (cached.Wifi.Status != ProvisionedWifiStatus.Locked)
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<NetworkFailureCode> failures = [.. cached.Failures];
        (ProvisionedWifiState wifi, WifiProfileDocument? profile) = ProvisionWifi(failures, cancellationToken);
        return cached with { Wifi = wifi, WifiProfile = profile, Failures = failures.ToArray() };
    }

    private async Task<Dot1xState> ProvisionWiredAsync(List<NetworkFailureCode> failures, CancellationToken cancellationToken)
    {
        ConnectDot1xSettings settings = _configuration.Dot1x;
        try
        {
            // Inside the try so a password the media carries for a disabled part is still cleared.
            if (!settings.IsEnabled)
            {
                return Dot1xState.NotConfigured;
            }

            // The wired startup prompt ran before provisioning (L4-12): a secret still locked means it was cancelled.
            if (_secrets.IsWiredLocked)
            {
                _logger.LogInformation("Wired 802.1X skipped because the deployment password was not entered.");
                return new Dot1xState(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.UnlockCancelled);
            }

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
            ClearWiredSecrets();
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

        CertificateStepResult certificates = InstallCertificates(
            ConnectCertificateSet.From(settings),
            _secrets.WiredCertificatePfxPassword,
            _secrets.WiredClientCertificate,
            NetworkFailureCode.WiredCertificateImportFailed,
            failures);
        if (certificates == CertificateStepResult.SecretUnavailable)
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
        try
        {
            // Inside the try so a passphrase or password the media carries for a disabled part is still cleared.
            if (!_configuration.Capabilities.WifiProvisioned || !settings.IsEnabled)
            {
                return (ProvisionedWifiState.NotConfigured, null);
            }

            // Not a failure: the profile is built by RefreshWifiAsync once the deployment password is entered.
            if (_secrets.IsWifiLocked)
            {
                return (LockedState(settings), null);
            }

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
            // Clear keeps locked items, so a part skipped while locked can still be unlocked later.
            _secrets.WifiPassphrase.Clear();
            _secrets.WifiCertificatePfxPassword.Clear();
            _secrets.WifiClientCertificate.Clear();
            _secrets.WifiEnterpriseProfile.Clear();
        }
    }

    private (ProvisionedWifiState State, WifiProfileDocument? Profile) ReadEnterpriseProfile(ConnectWifiSettings settings, List<NetworkFailureCode> failures)
    {
        // A protected template comes from the decrypted content; its .xml.encrypted file is not read again here.
        bool isTemplateProtected = settings.IsEnterpriseProfileTemplateProtected;
        string? templatePath = isTemplateProtected ? null : ResolveAssetPath(settings.EnterpriseProfileTemplatePath);
        if (isTemplateProtected)
        {
            if (_secrets.WifiEnterpriseProfile.Status != ConnectSecretStatus.Available)
            {
                failures.Add(NetworkFailureCode.WifiSecretUnavailable);
                return (Unavailable(ProvisionedWifiUnavailableReason.SecretUnavailable, isEnterprise: true), null);
            }
        }
        else if (templatePath is null || !File.Exists(templatePath))
        {
            failures.Add(NetworkFailureCode.ProvisionedProfileUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.ProfileTemplateMissing, isEnterprise: true), null);
        }

        CertificateStepResult certificates = InstallCertificates(
            ConnectCertificateSet.From(settings),
            _secrets.WifiCertificatePfxPassword,
            _secrets.WifiClientCertificate,
            NetworkFailureCode.WifiCertificateImportFailed,
            failures);
        if (certificates == CertificateStepResult.SecretUnavailable)
        {
            failures.Add(NetworkFailureCode.WifiSecretUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.SecretUnavailable, isEnterprise: true), null);
        }

        // A template without a readable name or SSID fails closed instead of guessing an identity.
        string xml = isTemplateProtected ? DecodeProtectedTemplate() : File.ReadAllText(templatePath!);
        WifiProfileDocument? profile = WifiProfileDocument.FromXml(xml);
        if (profile is null)
        {
            failures.Add(NetworkFailureCode.ProvisionedProfileUnavailable);
            return (Unavailable(ProvisionedWifiUnavailableReason.InvalidConfiguration, isEnterprise: true), null);
        }

        return (Ready(profile, isEnterprise: true), profile);
    }

    // Decodes the decrypted template as UTF-8 (without a byte order mark), then zeroes the content buffer.
    private string DecodeProtectedTemplate()
    {
        try
        {
            ReadOnlySpan<byte> content = _secrets.WifiEnterpriseProfile.Content;
            ReadOnlySpan<byte> preamble = Encoding.UTF8.Preamble;
            return Encoding.UTF8.GetString(content.StartsWith(preamble) ? content[preamble.Length..] : content);
        }
        finally
        {
            _secrets.WifiEnterpriseProfile.Clear();
        }
    }

    private (ProvisionedWifiState State, WifiProfileDocument? Profile) BuildPersonalProfile(ConnectWifiSettings settings, List<NetworkFailureCode> failures)
    {
        // L2-21: the configured SSID keeps today's trim; the passphrase is never trimmed (T9).
        WifiSsid? ssid = ConfiguredSsid(settings);
        WifiProfileSecurity? security = WifiSecurityClassifier.FromConfiguredSecurityType(settings.SecurityType);
        if (ssid is null || security is null)
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
    /// Imports every existing trusted root of <paramref name="certificates"/>, then its client certificate, and clears the
    /// PFX password and content.
    /// </summary>
    /// <returns>
    /// <see cref="CertificateStepResult.SecretUnavailable"/> when the client certificate needs a password or a protected
    /// content the media carries but that could not be read; the caller then stops its part and nothing is imported. An
    /// import failure only adds <paramref name="importFailure"/> and the remaining certificates are still imported.
    /// </returns>
    private CertificateStepResult InstallCertificates(
        ConnectCertificateSet certificates,
        ConnectSecret password,
        ConnectProtectedContent content,
        NetworkFailureCode importFailure,
        List<NetworkFailureCode> failures)
    {
        try
        {
            string? clientPath = certificates.IsClientCertificateContentProtected ? null : ResolveExistingAssetPath(certificates.ClientCertificatePath);
            bool hasClient = certificates.IsClientCertificateContentProtected || clientPath is not null;
            if (hasClient && IsClientSecretUnavailable(certificates, password, content, clientPath))
            {
                return CertificateStepResult.SecretUnavailable;
            }

            bool importFailed = false;
            foreach (string root in certificates.TrustedRootCertificatePaths)
            {
                if (ResolveExistingAssetPath(root) is string rootPath)
                {
                    importFailed |= !Record(CertificateInstaller.GetCertificateKind(rootPath), _certificates.Install(rootPath, ReadOnlySpan<char>.Empty));
                }
            }

            ReadOnlySpan<char> clientPassword = password.Status == ConnectSecretStatus.Available ? password.Value : ReadOnlySpan<char>.Empty;
            if (certificates.IsClientCertificateContentProtected)
            {
                importFailed |= !Record("pfx", _certificates.InstallPfx(content.Content, clientPassword));
            }
            else if (clientPath is not null)
            {
                importFailed |= !Record(CertificateInstaller.GetCertificateKind(clientPath), _certificates.Install(clientPath, clientPassword));
            }

            if (importFailed)
            {
                failures.Add(importFailure);
            }

            return CertificateStepResult.Completed;
        }
        finally
        {
            password.Clear();
            content.Clear();
        }
    }

    // A protected client needs its decrypted content; a plain PFX (lot 2 rule) needs the password the media carries.
    private static bool IsClientSecretUnavailable(ConnectCertificateSet certificates, ConnectSecret password, ConnectProtectedContent content, string? clientPath)
    {
        bool passwordUnavailable = certificates.ClientCertificatePasswordSecret is not null && password.Status == ConnectSecretStatus.Unavailable;
        return certificates.IsClientCertificateContentProtected
            ? content.Status != ConnectSecretStatus.Available || passwordUnavailable
            : NetworkProfileRoamingArtifacts.IsPfxPath(clientPath!) && passwordUnavailable;
    }

    private bool Record(string certificateKind, CertificateInstallResult result)
    {
        _logger.LogDebug(
            "Provisioned certificate processed. CertificateKind={CertificateKind}, CertificateInstallStatus={CertificateInstallStatus}",
            certificateKind,
            result.Status);
        return result.IsSuccess;
    }

    private void ClearWiredSecrets()
    {
        _secrets.WiredCertificatePfxPassword.Clear();
        _secrets.WiredClientCertificate.Clear();
    }

    private string? ResolveAssetPath(string? value) => ProvisionedWifiProfileResolver.ResolveAssetPath(value, _configurationPath);

    private string? ResolveExistingAssetPath(string? value) => ResolveAssetPath(value) is string path && File.Exists(path) ? path : null;

    // The exception is not attached: a template read error names the template file, often "Wi-Fi-<SSID>.xml".
    private void LogUnexpectedFailure(Exception exception, string part)
    {
        NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.Describe(NetworkFailureCode.Unexpected);
        _logger.LogWarning(
            "Network provisioning part failed unexpectedly. ProvisioningPart={ProvisioningPart}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, FailureType={FailureType}, ErrorCode={ErrorCode}",
            part,
            descriptor.Kind,
            descriptor.Reason,
            descriptor.Code,
            exception.GetType().Name,
            NetworkFailureTaxonomy.ExceptionErrorCode(exception));
    }

    // L2-21: the configured SSID is trimmed; an empty or over-long value has no SSID.
    private static WifiSsid? ConfiguredSsid(ConnectWifiSettings settings)
    {
        string? text = settings.Ssid?.Trim();
        return !string.IsNullOrEmpty(text) && WifiSsid.TryFromBytes(Encoding.UTF8.GetBytes(text), out WifiSsid? ssid) ? ssid : null;
    }

    // A locked enterprise template has no readable name, SSID or hidden flag; only the configured SSID is known (L4-11).
    private ProvisionedWifiState LockedState(ConnectWifiSettings settings) => new(
        ProvisionedWifiStatus.Locked,
        _secrets.IsUnlockDeclined ? ProvisionedWifiUnavailableReason.UnlockCancelled : ProvisionedWifiUnavailableReason.None,
        ConfiguredSsid(settings),
        IsEnterprise: settings.HasEnterpriseProfile,
        IsHidden: false);

    private static ProvisionedWifiState Ready(WifiProfileDocument profile, bool isEnterprise) =>
        new(ProvisionedWifiStatus.Ready, ProvisionedWifiUnavailableReason.None, profile.Ssid, profile.Name, isEnterprise, profile.IsHidden);

    private static ProvisionedWifiState Unavailable(ProvisionedWifiUnavailableReason reason, bool isEnterprise, WifiSsid? ssid = null) =>
        new(ProvisionedWifiStatus.Unavailable, reason, ssid, IsEnterprise: isEnterprise);
}

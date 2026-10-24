// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Serilog;

namespace Foundry.Services.Secrets;

/// <summary>
/// Remembers the passwords of the secret state services in the local vault: it restores them at startup and saves them, debounced,
/// whenever a password or the configuration it belongs to changes.
/// </summary>
/// <remarks>
/// Entries come from <see cref="SecretMap"/>, so a certificate password follows its file hash and a password whose owner left the
/// configuration is dropped at the next save. Only confirmed passwords are saved. An unreadable vault restores nothing, reports
/// <see cref="VaultStatus.Unreadable"/> and is replaced only when a password is saved again, never by an empty vault. A vault
/// that cannot be read because of an I/O failure is never overwritten during the session, so a transient lock cannot wipe it.
/// Configuration saving never depends on the vault. Secret values are never logged.
/// </remarks>
internal sealed class SecretVaultBridge : ISecretVaultBridge, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly LocalSecretVault vault;
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IDeploymentProtectionSecretStateService protection;
    private readonly INetworkSecretStateService network;
    private readonly IOobeAccountSecretStateService oobe;
    private readonly IDomainJoinSecretStateService domainJoin;
    private readonly IAutopilotHardwareHashSessionState autopilot;
    private readonly IAppDispatcher dispatcher;
    private readonly ILogger logger;
    private readonly Timer saveTimer;
    private IReadOnlyDictionary<string, string>? savedEntries;
    private bool isRestoring;
    private bool isSaveBlocked;
    private bool isSavePending;
    private bool isInitialized;
    private bool isDisposed;

    public SecretVaultBridge(
        LocalSecretVault vault,
        IFoundryConfigurationStateService configuration,
        IDeploymentProtectionSecretStateService protection,
        INetworkSecretStateService network,
        IOobeAccountSecretStateService oobe,
        IDomainJoinSecretStateService domainJoin,
        IAutopilotHardwareHashSessionState autopilot,
        IAppDispatcher dispatcher,
        ILogger logger)
    {
        this.vault = vault;
        this.configuration = configuration;
        this.protection = protection;
        this.network = network;
        this.oobe = oobe;
        this.domainJoin = domainJoin;
        this.autopilot = autopilot;
        this.dispatcher = dispatcher;
        this.logger = logger.ForContext<SecretVaultBridge>();
        saveTimer = new Timer(OnSaveTimerElapsed);
    }

    /// <inheritdoc />
    public VaultStatus Status { get; private set; } = VaultStatus.Empty;

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Loads the vault, restores its passwords into the secret state services and starts following their changes.
    /// </summary>
    /// <remarks>Call once on the UI thread, after the configuration state and the legacy migration have run.</remarks>
    public void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        IReadOnlyDictionary<string, string> entries;
        try
        {
            entries = vault.Load(out VaultStatus status);
            Status = status;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Secret vault could not be read. Saved passwords are not restored and password changes are not saved during this session.");
            entries = new Dictionary<string, string>(StringComparer.Ordinal);
            Status = VaultStatus.Unreadable;
            isSaveBlocked = true;
        }

        savedEntries = Status == VaultStatus.Unreadable ? null : entries;
        if (Status == VaultStatus.Unreadable && !isSaveBlocked)
        {
            logger.Warning("Secret vault is unreadable. Saved passwords must be entered again; the vault is replaced at the next password save.");
        }

        Restore(entries);
        configuration.StateChanged += OnChanged;
        protection.Changed += OnChanged;
        network.Changed += OnChanged;
        oobe.Changed += OnChanged;
        domainJoin.Changed += OnChanged;
        autopilot.BootMediaCertificatePasswordChanged += OnChanged;
        logger.Information("Secret vault loaded. Status={Status}, EntryCount={EntryCount}", Status, entries.Count);
    }

    public void Dispose()
    {
        if (isDisposed) return;
        try
        {
            if (isSavePending) Save();
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Pending password changes could not be saved to the secret vault at shutdown.");
        }

        isDisposed = true;
        saveTimer.Dispose();
        if (!isInitialized) return;
        configuration.StateChanged -= OnChanged;
        protection.Changed -= OnChanged;
        network.Changed -= OnChanged;
        oobe.Changed -= OnChanged;
        domainJoin.Changed -= OnChanged;
        autopilot.BootMediaCertificatePasswordChanged -= OnChanged;
    }

    private void Restore(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0) return;
        FoundryConfigurationDocument current = configuration.Current;
        SecretSnapshot secrets = SecretMap.FromEntries(current, entries);
        isRestoring = true;
        try
        {
            if (secrets.DeploymentProtection is { } deploymentPassword)
            {
                protection.SetPassword(deploymentPassword);
                protection.SetConfirmation(deploymentPassword);
            }

            network.Update(current.Network, new NetworkSecrets
            {
                WifiPassphrase = secrets.WifiPassphrase,
                WiredCertificatePfxPassword = secrets.Dot1xCertificatePassword,
                WifiCertificatePfxPassword = secrets.WifiCertificatePassword
            });
            if (secrets.AutopilotCertificatePassword is { } autopilotPassword)
            {
                autopilot.SetBootMediaCertificatePassword(current.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx, autopilotPassword);
            }

            if (secrets.OobeAdministrator is { } administratorPassword)
            {
                oobe.SetAdministratorPassword(administratorPassword);
                oobe.SetAdministratorConfirmation(administratorPassword);
            }

            foreach ((string accountId, string password) in secrets.OobeAccounts)
            {
                oobe.SetAdditionalAccountPassword(accountId, password);
                oobe.SetAdditionalAccountConfirmation(accountId, password);
            }

            foreach ((string account, string password) in secrets.DomainAccounts)
            {
                try
                {
                    domainJoin.SetPassword(account, password);
                }
                catch (ArgumentException)
                {
                    logger.Warning("A saved domain join password no longer satisfies the password rules and was not restored.");
                }
            }
        }
        finally
        {
            isRestoring = false;
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (isRestoring || isDisposed) return;
        isSavePending = true;
        saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnSaveTimerElapsed(object? state)
    {
        try
        {
            dispatcher.TryEnqueue(() =>
            {
                if (!isDisposed) Save();
            });
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Password changes could not be scheduled for the secret vault; they are saved at the next change.");
        }
    }

    /// <summary>Writes the current passwords when they differ from the saved ones. Runs on the UI thread, which owns the state services.</summary>
    private void Save()
    {
        isSavePending = false;
        if (isSaveBlocked) return;
        IReadOnlyDictionary<string, string> entries = SecretMap.ToEntries(configuration.Current, CaptureSecrets());
        bool unchanged = savedEntries is null
            ? entries.Count == 0
            : savedEntries.Count == entries.Count && entries.All(entry =>
                savedEntries.TryGetValue(entry.Key, out string? saved) && string.Equals(saved, entry.Value, StringComparison.Ordinal));
        if (unchanged) return;

        try
        {
            vault.Save(entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.Warning(exception, "Secret vault could not be saved; the passwords are saved at the next change. EntryCount={EntryCount}", entries.Count);
            return;
        }

        savedEntries = entries;
        logger.Debug("Secret vault saved. EntryCount={EntryCount}", entries.Count);
        if (Status != VaultStatus.Ok)
        {
            Status = VaultStatus.Ok;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private SecretSnapshot CaptureSecrets()
    {
        FoundryConfigurationDocument current = configuration.Current;
        NetworkSecrets networkSecrets = network.GetSecrets(current.Network);
        var oobeAccounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (OobeAdditionalAccountSettings account in current.Customization.Oobe.AdditionalAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.Id)) continue;
            if (Confirmed(oobe.GetAdditionalAccountPasswordCopy(account.Id), oobe.GetAdditionalAccountConfirmationCopy(account.Id)) is { } password)
                oobeAccounts[account.Id] = password;
        }

        var domainAccounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string account in current.DomainJoin.GetStoredAccountNames())
        {
            if (domainJoin.GetPasswordCopy(account) is { } password) domainAccounts[account] = Consume(password);
        }

        return new SecretSnapshot
        {
            DeploymentProtection = protection.IsValid ? Consume(protection.GetConfirmedPasswordCopy()) : null,
            WifiPassphrase = networkSecrets.WifiPassphrase,
            Dot1xCertificatePassword = networkSecrets.WiredCertificatePfxPassword,
            WifiCertificatePassword = networkSecrets.WifiCertificatePfxPassword,
            AutopilotCertificatePassword = autopilot.GetBootMediaCertificatePassword(current.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx),
            OobeAdministrator = Confirmed(oobe.GetAdministratorPasswordCopy(), oobe.GetAdministratorConfirmationCopy()),
            OobeAccounts = oobeAccounts,
            DomainAccounts = domainAccounts
        };
    }

    /// <summary>Returns the password when it is set and matches its confirmation, and clears both buffers.</summary>
    private static string? Confirmed(char[] password, char[] confirmation)
    {
        try
        {
            return password.Length > 0 && password.AsSpan().SequenceEqual(confirmation) ? new string(password) : null;
        }
        finally
        {
            Clear(password);
            Clear(confirmation);
        }
    }

    private static string Consume(char[] buffer)
    {
        try
        {
            return new string(buffer);
        }
        finally
        {
            Clear(buffer);
        }
    }

    private static void Clear(char[] buffer) => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));
}

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
/// The single writer of the local secret vault once started: it restores the configuration passwords into the secret state
/// services, saves them, debounced, whenever a password or the configuration it belongs to changes, and serves the entries it
/// does not own (sharing keys, future sections) through <see cref="ReadEntries"/> and <see cref="WriteEntries"/>.
/// </summary>
/// <remarks>
/// Saves rebuild the owned entries with <see cref="SecretMap.ToEntries"/> and combine them with <see cref="SecretMap.Merge"/>, so
/// every id the map does not own, and every sharing id, is kept; a certificate password follows its file hash and a password whose
/// owner left the configuration is dropped. Only confirmed passwords are saved. An unreadable vault restores nothing, reports
/// <see cref="VaultStatus.Unreadable"/> and is replaced only when a password is saved again, never by an empty vault. A vault that
/// cannot be read because of an I/O failure is never overwritten during the session, so a transient lock cannot wipe it.
/// Configuration saving never depends on the vault. All members run on the UI thread, which owns the secret state services, so
/// saves and the entry methods never interleave. Secret values are never logged.
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
    private IReadOnlyDictionary<string, string> vaultEntries = new Dictionary<string, string>(StringComparer.Ordinal);
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
    /// <remarks>Call once on the UI thread, after the configuration state and the legacy migration have run. It never throws.</remarks>
    public void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        try
        {
            vaultEntries = vault.Load(out VaultStatus status);
            Status = status;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Secret vault could not be read. Saved passwords are not restored and password changes are not saved during this session.");
            Status = VaultStatus.Unreadable;
            isSaveBlocked = true;
        }

        if (Status == VaultStatus.Unreadable && !isSaveBlocked)
        {
            logger.Warning("Secret vault is unreadable. Saved passwords must be entered again; the vault is replaced at the next password save.");
        }

        Restore(vaultEntries);
        configuration.StateChanged += OnChanged;
        protection.Changed += OnChanged;
        network.Changed += OnChanged;
        oobe.Changed += OnChanged;
        domainJoin.Changed += OnChanged;
        autopilot.BootMediaCertificatePasswordChanged += OnChanged;
        logger.Information("Secret vault loaded. Status={Status}, EntryCount={EntryCount}", Status, vaultEntries.Count);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (isSavePending) Save();
        return new Dictionary<string, string>(vaultEntries, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void WriteEntries(IReadOnlyDictionary<string, string> entries, IEnumerable<string>? removeIds = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(entries);
        string[] removed = removeIds?.ToArray() ?? [];
        if (removed.Any(SecretMap.IsOwned)) throw new ArgumentException("Owned passwords are removed through their secret state service.", nameof(removeIds));
        if (isSaveBlocked) throw new IOException("SecretVault.Unavailable");

        if (isSavePending) Save();
        var merged = new Dictionary<string, string>(vaultEntries, StringComparer.Ordinal);
        foreach (string id in removed) merged.Remove(id);
        foreach ((string id, string value) in entries) merged[id] = value;
        Persist(merged);
        Restore(entries.Where(entry => SecretMap.IsOwned(entry.Key)).ToDictionary(StringComparer.Ordinal));
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

    /// <summary>Pushes the owned passwords of <paramref name="entries"/> into the secret state services; never throws.</summary>
    private void Restore(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0) return;
        isRestoring = true;
        try
        {
            FoundryConfigurationDocument current = configuration.Current;
            SecretSnapshot secrets = SecretMap.FromEntries(current, entries);
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
        catch (Exception exception)
        {
            // The exception never carries a password value; the remaining passwords simply stay to be entered again.
            logger.Warning("Saved passwords could not all be restored into the secret state services. ExceptionType={ExceptionType}", exception.GetType().Name);
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

    /// <summary>Writes the current passwords merged with the entries the bridge does not own, when that changes the vault.</summary>
    /// <remarks>A failed write keeps the save pending, so the next change or the shutdown retries it.</remarks>
    private void Save()
    {
        if (isSaveBlocked)
        {
            isSavePending = false;
            return;
        }

        IReadOnlyDictionary<string, string> owned = SecretMap.ToEntries(configuration.Current, CaptureSecrets());
        IReadOnlyDictionary<string, string> merged = SecretMap.Merge(vaultEntries, owned);
        // Never replace an unreadable vault with an empty one.
        bool unchanged = Status == VaultStatus.Unreadable ? merged.Count == 0 : SameEntries(vaultEntries, merged);
        if (unchanged)
        {
            isSavePending = false;
            return;
        }

        try
        {
            Persist(merged);
            isSavePending = false;
            logger.Debug("Secret vault saved. EntryCount={EntryCount}", merged.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            isSavePending = true;
            logger.Warning(exception, "Secret vault could not be saved; the passwords are saved at the next change. EntryCount={EntryCount}", merged.Count);
        }
    }

    private void Persist(IReadOnlyDictionary<string, string> entries)
    {
        vault.Save(entries);
        vaultEntries = entries;
        if (Status != VaultStatus.Ok)
        {
            Status = VaultStatus.Ok;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool SameEntries(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && right.All(entry =>
            left.TryGetValue(entry.Key, out string? value) && string.Equals(value, entry.Value, StringComparison.Ordinal));

    private SecretSnapshot CaptureSecrets()
    {
        FoundryConfigurationDocument current = configuration.Current;
        NetworkSecrets networkSecrets = network.GetRetainedSecrets(current.Network);
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

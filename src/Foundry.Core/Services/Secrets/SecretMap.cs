// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Secrets;

/// <summary>Converts the configuration passwords between <see cref="SecretSnapshot"/> fields and vault entries keyed by <see cref="SecretIds"/>.</summary>
/// <remarks>
/// The map is a table of bindings, one per secret field: a section that gains a password adds a row, not a code path. Each id is
/// derived from the configuration, so a password only maps while its owner exists: a certificate password is keyed by the current
/// certificate content hash, an OOBE password by an account id the configuration lists, and a domain password by the canonical
/// name of an account the configuration stores. A password whose owner is gone is neither written nor read back.
/// </remarks>
public static class SecretMap
{
    private static readonly SecretBinding[] Bindings =
    [
        Single(SecretIds.DeploymentProtection, secrets => secrets.DeploymentProtection, (secrets, value) => secrets with { DeploymentProtection = value },
            configuration => configuration.General.DeploymentProtection.IsEnabled),
        Single(SecretIds.WifiPassphrase, secrets => secrets.WifiPassphrase, (secrets, value) => secrets with { WifiPassphrase = value },
            configuration => NetworkMediaReadinessEvaluator.RequiresPersonalWifiPassphrase(configuration.Network)),
        Certificate(configuration => configuration.Network.Dot1x.Certificate, SecretIds.Dot1xCertificate,
            secrets => secrets.Dot1xCertificatePassword, (secrets, value) => secrets with { Dot1xCertificatePassword = value },
            configuration => configuration.Network.Dot1x is { IsEnabled: true, RequiresCertificate: true }),
        Certificate(configuration => configuration.Network.Wifi.Certificate, SecretIds.WifiCertificate,
            secrets => secrets.WifiCertificatePassword, (secrets, value) => secrets with { WifiCertificatePassword = value },
            configuration => configuration.Network.WifiProvisioned && configuration.Network.Wifi is { IsEnabled: true, RequiresCertificate: true }),
        Certificate(configuration => configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx, SecretIds.AutopilotCertificate,
            secrets => secrets.AutopilotCertificatePassword, (secrets, value) => secrets with { AutopilotCertificatePassword = value },
            configuration => configuration.Autopilot is { IsEnabled: true, ProvisioningMode: AutopilotProvisioningMode.HardwareHashUpload }),
        Single(SecretIds.OobeAdministrator, secrets => secrets.OobeAdministrator, (secrets, value) => secrets with { OobeAdministrator = value },
            configuration => configuration.Customization.Oobe is { IsEnabled: true, EnableAdministratorAccount: true, UseAdministratorPassword: true }),
        new(
            Keys: configuration => configuration.Customization.Oobe.AdditionalAccounts.Select(account => account.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal),
            Id: SecretIds.OobeAccount,
            Get: (secrets, id) => secrets.OobeAccounts.GetValueOrDefault(id),
            Set: (secrets, id, value) => secrets with { OobeAccounts = With(secrets.OobeAccounts, id, value) },
            RequiredWhen: (configuration, id) => configuration.Customization.Oobe.IsEnabled &&
                configuration.Customization.Oobe.AdditionalAccounts.Any(account => account.Id == id && account.UsePassword)),
        new(
            Keys: configuration => configuration.DomainJoin.GetStoredAccountNames(),
            Id: SecretIds.DomainAccount,
            Get: (secrets, account) => secrets.DomainAccounts
                .FirstOrDefault(pair => DomainJoinCredentialContext.CanonicalizeAccountName(pair.Key) == account).Value,
            Set: (secrets, account, value) => secrets with { DomainAccounts = With(secrets.DomainAccounts, account, value) },
            RequiredWhen: (configuration, account) => configuration.DomainJoin.GetReferencedAccountNames().Contains(account, StringComparer.Ordinal)),
    ];

    /// <summary>Returns the vault entries for the passwords of <paramref name="secrets"/> whose owner exists in <paramref name="configuration"/>.</summary>
    /// <remarks>An empty password is kept: a PFX can have one. A <see langword="null"/> password produces no entry.</remarks>
    public static IReadOnlyDictionary<string, string> ToEntries(FoundryConfigurationDocument configuration, SecretSnapshot secrets)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secrets);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SecretBinding binding in Bindings)
        {
            foreach (string key in binding.Keys(configuration))
            {
                if (binding.Get(secrets, key) is { } value) entries[binding.Id(key)] = value;
            }
        }
        return entries;
    }

    /// <summary>Reads back the passwords of <paramref name="entries"/> that belong to <paramref name="configuration"/>; other entries are ignored.</summary>
    public static SecretSnapshot FromEntries(FoundryConfigurationDocument configuration, IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(entries);
        var secrets = new SecretSnapshot();
        foreach (SecretBinding binding in Bindings)
        {
            foreach (string key in binding.Keys(configuration))
            {
                if (entries.TryGetValue(binding.Id(key), out string? value)) secrets = binding.Set(secrets, key, value);
            }
        }
        return secrets;
    }

    /// <summary>Returns the ids of the passwords that media built from <paramref name="configuration"/> needs; disabled features need none.</summary>
    public static IReadOnlyList<string> RequiredIds(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Bindings
            .SelectMany(binding => binding.Keys(configuration).Where(key => binding.RequiredWhen(configuration, key)).Select(binding.Id))
            .ToArray();
    }

    private static SecretBinding Single(
        string id,
        Func<SecretSnapshot, string?> get,
        Func<SecretSnapshot, string, SecretSnapshot> set,
        Func<FoundryConfigurationDocument, bool> requiredWhen) =>
        new(_ => [string.Empty], _ => id, (secrets, _) => get(secrets), (secrets, _, value) => set(secrets, value),
            (configuration, _) => requiredWhen(configuration));

    private static SecretBinding Certificate(
        Func<FoundryConfigurationDocument, AuthoringFileReference?> certificate,
        Func<string, string> id,
        Func<SecretSnapshot, string?> get,
        Func<SecretSnapshot, string, SecretSnapshot> set,
        Func<FoundryConfigurationDocument, bool> requiredWhen) =>
        new(configuration => certificate(configuration)?.ContentHash is { Length: > 0 } hash ? [hash] : [],
            id, (secrets, _) => get(secrets), (secrets, _, value) => set(secrets, value), (configuration, _) => requiredWhen(configuration));

    private static Dictionary<string, string> With(IReadOnlyDictionary<string, string> source, string key, string value) =>
        new(source, StringComparer.Ordinal) { [key] = value };

    /// <summary>Binds one secret field to its vault ids.</summary>
    /// <param name="Keys">The owners of the field present in a configuration: none, one, or one per account.</param>
    /// <param name="Id">Builds the vault id of an owner.</param>
    /// <param name="Get">Reads the password of an owner from a snapshot.</param>
    /// <param name="Set">Returns a snapshot holding the password of an owner.</param>
    /// <param name="RequiredWhen">Whether media built from the configuration needs the password of an owner.</param>
    private sealed record SecretBinding(
        Func<FoundryConfigurationDocument, IEnumerable<string>> Keys,
        Func<string, string> Id,
        Func<SecretSnapshot, string, string?> Get,
        Func<SecretSnapshot, string, string, SecretSnapshot> Set,
        Func<FoundryConfigurationDocument, string, bool> RequiredWhen);
}

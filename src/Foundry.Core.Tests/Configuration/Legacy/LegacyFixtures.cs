// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Core.Tests.Configuration.Legacy;

/// <summary>Values the legacy fixtures were generated with. Every secret is an obviously fake test value.</summary>
internal sealed record LegacyFixtureValues
{
    public string ExportPassphrase { get; init; } = string.Empty;
    public string ConnectionPassphrase { get; init; } = string.Empty;
    public string WifiSsid { get; init; } = string.Empty;
    public string WifiPassphrase { get; init; } = string.Empty;
    public string DeploymentPassword { get; init; } = string.Empty;
    public string AdministratorPassword { get; init; } = string.Empty;
    public string OobeAccountId { get; init; } = string.Empty;
    public string OobeAccountPassword { get; init; } = string.Empty;
    public string WiredCertificatePassword { get; init; } = string.Empty;
    public string WiredCertificateBase64 { get; init; } = string.Empty;
    public string DomainAccount { get; init; } = string.Empty;
    public string DomainPassword { get; init; } = string.Empty;
    public string UnattendId { get; init; } = string.Empty;
    public string DriverSetId { get; init; } = string.Empty;
    public string ConnectionKeyBase64 { get; init; } = string.Empty;
    public Guid ActiveLocalId { get; init; }
    public string ActiveDisplayName { get; init; } = string.Empty;
    public Guid SharedLocalId { get; init; }
    public string SharedDisplayName { get; init; } = string.Empty;
}

/// <summary>Locates the committed legacy fixtures copied to the test output directory.</summary>
internal static class LegacyFixtures
{
    public const string ExportWithSecrets = "export-with-secrets.foundryprofile";
    public const string ExportWithoutSecrets = "export-without-secrets.foundryprofile";
    public const string Connection = "connection.foundryprofile";
    public const string LocalStore = "local-store";
    public const string LocalStoreCredentials = "local-store-credentials.json";
    public const string Values = "fixtures.json";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Directory => System.IO.Path.Combine(AppContext.BaseDirectory, "Configuration", "Legacy", "Fixtures");

    public static string Path(string name) => System.IO.Path.Combine(Directory, name);

    public static byte[] Read(string name) => File.ReadAllBytes(Path(name));

    public static LegacyFixtureValues LoadValues() =>
        JsonSerializer.Deserialize<LegacyFixtureValues>(Read(Values), JsonOptions) ?? throw new InvalidDataException("The fixture values are missing.");
}

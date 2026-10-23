// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Secrets;

public sealed class LocalSecretVaultTests : IDisposable
{
    private readonly TemporaryDirectory temp = new();

    private string VaultPath => Path.Combine(temp.Path, "Configuration", "secrets.dat");

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        var vault = new LocalSecretVault(VaultPath);
        var entries = new Dictionary<string, string>
        {
            [SecretIds.WifiPassphrase] = "correct horse",
            [SecretIds.OobeAccount("a1")] = "pässwörd 日本",
        };

        vault.Save(entries);
        IReadOnlyDictionary<string, string> loaded = vault.Load(out VaultStatus status);

        Assert.Equal(VaultStatus.Ok, status);
        Assert.Equal(entries, loaded);
        Assert.False(File.ReadAllBytes(VaultPath).AsSpan().IndexOf("correct horse"u8) >= 0);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyWithEmptyStatus()
    {
        var vault = new LocalSecretVault(VaultPath);

        IReadOnlyDictionary<string, string> loaded = vault.Load(out VaultStatus status);

        Assert.Empty(loaded);
        Assert.Equal(VaultStatus.Empty, status);
    }

    [Fact]
    public void Load_UnreadableVault_ReturnsEmptyWithUnreadableStatus()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VaultPath)!);
        byte[] garbage = RandomNumberGenerator.GetBytes(256);
        File.WriteAllBytes(VaultPath, garbage);
        var vault = new LocalSecretVault(VaultPath);

        IReadOnlyDictionary<string, string> loaded = vault.Load(out VaultStatus status);

        Assert.Empty(loaded);
        Assert.Equal(VaultStatus.Unreadable, status);
        Assert.Equal(garbage, File.ReadAllBytes(VaultPath));
    }

    [Fact]
    public void Load_ProtectedButWrongShape_ReturnsEmptyWithUnreadableStatus()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VaultPath)!);
        File.WriteAllBytes(VaultPath, ProtectedData.Protect("[1,2]"u8.ToArray(), "Foundry.SecretVault.v1"u8.ToArray(), DataProtectionScope.CurrentUser));
        var vault = new LocalSecretVault(VaultPath);

        IReadOnlyDictionary<string, string> loaded = vault.Load(out VaultStatus status);

        Assert.Empty(loaded);
        Assert.Equal(VaultStatus.Unreadable, status);
    }

    [Fact]
    public void Save_ReplacesAtomically()
    {
        var vault = new LocalSecretVault(VaultPath);
        vault.Save(new Dictionary<string, string> { ["a/b"] = "1" });

        vault.Save(new Dictionary<string, string> { ["a/b"] = "2" });

        Assert.Equal("2", vault.Load(out _)["a/b"]);
        Assert.Equal(VaultPath, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(VaultPath)!)));
    }

    [Fact]
    public void Save_AfterUnreadableVault_ReplacesIt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VaultPath)!);
        File.WriteAllBytes(VaultPath, RandomNumberGenerator.GetBytes(64));
        var vault = new LocalSecretVault(VaultPath);
        _ = vault.Load(out _);

        vault.Save(new Dictionary<string, string> { ["x/y"] = "z" });

        Assert.Equal("z", vault.Load(out VaultStatus status)["x/y"]);
        Assert.Equal(VaultStatus.Ok, status);
    }

    [Fact]
    public void SecretIds_BuildLogicalIds()
    {
        Assert.Equal("wifi/certificate/abc", SecretIds.WifiCertificate("ABC"));
        Assert.Equal("dot1x/certificate/abc", SecretIds.Dot1xCertificate("abc"));
        Assert.Equal("autopilot/certificate/abc", SecretIds.AutopilotCertificate("abc"));
        Assert.Equal("oobe/account/id1", SecretIds.OobeAccount("id1"));
        Assert.Equal(@"domain/CONTOSO\admin", SecretIds.DomainAccount(@"CONTOSO\admin"));
        Assert.True(SecretIds.IsSharingInternal(SecretIds.SigningKey));
        Assert.True(SecretIds.IsSharingInternal(SecretIds.PublicationPassword));
        Assert.True(SecretIds.IsSharingInternal(SecretIds.MasterKey));
        Assert.False(SecretIds.IsSharingInternal(SecretIds.WifiPassphrase));
        Assert.False(SecretIds.IsSharingInternal("sharingx/other"));
        Assert.Throws<ArgumentNullException>(() => SecretIds.IsSharingInternal(null!));
    }

    public void Dispose() => temp.Dispose();
}

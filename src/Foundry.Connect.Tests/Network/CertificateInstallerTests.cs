// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class CertificateInstallerTests
{
    private const X509KeyStorageFlags ProductionFlags = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet;

    [Fact]
    public void Install_Pfx_AddsToPersonalStoreWithOnePersistingLoad()
    {
        using var directory = new TemporaryDirectory();
        var store = new FakeCertificateStore();
        var loader = new RecordingPkcs12Loader();

        CertificateInstallResult result = CreateInstaller(store, loader).Install(CreateCertificateFiles(directory).PfxPath, "pfx-password");

        Assert.Equal(CertificateInstallStatus.Installed, result.Status);
        (StoreName storeName, _, X509Certificate2 added) = Assert.Single(store.Added);
        Assert.Equal(StoreName.My, storeName);
        Assert.Equal(IntPtr.Zero, added.Handle);
        Assert.Equal(1, loader.Loads.Count(static load => load.Flags.HasFlag(X509KeyStorageFlags.PersistKeySet)));
        Assert.All(loader.Loads, static load => Assert.Equal(IntPtr.Zero, load.Certificate.Handle));
    }

    [Fact]
    public void Install_SamePfxTwice_SecondCallIsAlreadyInstalledInThisProcess()
    {
        using var directory = new TemporaryDirectory();
        string pfxPath = CreateCertificateFiles(directory).PfxPath;
        var store = new FakeCertificateStore();
        var loader = new RecordingPkcs12Loader();
        CertificateInstaller installer = CreateInstaller(store, loader);

        CertificateInstallResult first = installer.Install(pfxPath, "pfx-password");
        int containsCallsAfterFirst = store.ContainsCalls;
        int loadsAfterFirst = loader.Loads.Count;
        CertificateInstallResult second = installer.Install(pfxPath, "pfx-password");

        Assert.Equal(CertificateInstallStatus.Installed, first.Status);
        Assert.Equal(CertificateInstallStatus.AlreadyPresent, second.Status);
        Assert.Single(store.Added);
        // The fake store never reports what was added, so only the in-process memory can answer the second call.
        Assert.Equal(containsCallsAfterFirst, store.ContainsCalls);
        Assert.All(loader.Loads.Skip(loadsAfterFirst), static load => Assert.Equal(X509KeyStorageFlags.EphemeralKeySet, load.Flags));
    }

    [Fact]
    public void Install_PfxAlreadyInStore_IsAlreadyPresentWithoutPersistingLoad()
    {
        using var directory = new TemporaryDirectory();
        (string pfxPath, _, string thumbprint) = CreateCertificateFiles(directory);
        var store = new FakeCertificateStore();
        store.Present.Add((StoreName.My, thumbprint));
        var loader = new RecordingPkcs12Loader();

        CertificateInstallResult result = CreateInstaller(store, loader).Install(pfxPath, "pfx-password");

        Assert.Equal(CertificateInstallStatus.AlreadyPresent, result.Status);
        Assert.Equal(1, store.ContainsCalls);
        Assert.Empty(store.Added);
        RecordedPkcs12Load load = Assert.Single(loader.Loads);
        Assert.Equal(X509KeyStorageFlags.EphemeralKeySet, load.Flags);
        Assert.Equal(IntPtr.Zero, load.Certificate.Handle);
    }

    [Fact]
    public void Install_PublicCertificate_AddsToRootStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new FakeCertificateStore();
        var loader = new RecordingPkcs12Loader();

        CertificateInstallResult result = CreateInstaller(store, loader).Install(CreateCertificateFiles(directory).CerPath, ReadOnlySpan<char>.Empty);

        Assert.Equal(CertificateInstallStatus.Installed, result.Status);
        Assert.Equal(StoreName.Root, Assert.Single(store.Added).StoreName);
        Assert.Empty(loader.Loads);
    }

    [Fact]
    public void Install_WrongPfxPassword_FailsWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RecordingLogger<CertificateInstaller>();
        var loader = new RecordingPkcs12Loader();
        var installer = new CertificateInstaller(new FakeCertificateStore(), logger, ProductionFlags, loader.Load);

        CertificateInstallResult result = installer.Install(CreateCertificateFiles(directory).PfxPath, "wrong-password");

        Assert.Equal(CertificateInstallStatus.Failed, result.Status);
        Assert.DoesNotContain(loader.Attempts, static flags => flags.HasFlag(X509KeyStorageFlags.PersistKeySet));
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("pfx", warning.Properties["CertificateKind"]);
        Assert.Equal(nameof(CryptographicException), warning.Properties["FailureType"]);
        Assert.DoesNotContain("wrong-password", warning.Message, StringComparison.Ordinal);
        // Author file names can carry organization names; neither the message nor a property may contain them.
        Assert.DoesNotContain("client", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(warning.Properties.Values, static value => Assert.DoesNotContain("client", value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"C:\media\client.pfx", true)]
    [InlineData(@"C:\media\client.P12", true)]
    [InlineData(@"C:\media\root.cer", false)]
    [InlineData(@"C:\media\pfx", false)]
    public void IsPfxPath_ComparesExtensionIgnoringCase(string path, bool expected) =>
        Assert.Equal(expected, CertificateInstaller.IsPfxPath(path));

    // Production flags: the recording loader substitutes EphemeralKeySet, so no test writes a key to disk.
    private static CertificateInstaller CreateInstaller(FakeCertificateStore store, RecordingPkcs12Loader loader) =>
        new(store, new RecordingLogger<CertificateInstaller>(), ProductionFlags, loader.Load);

    private static (string PfxPath, string CerPath, string Thumbprint) CreateCertificateFiles(TemporaryDirectory directory)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Foundry Connect Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (
            directory.CreateFile("client.pfx", certificate.Export(X509ContentType.Pfx, "pfx-password")),
            directory.CreateFile("root.cer", certificate.Export(X509ContentType.Cert)),
            certificate.Thumbprint);
    }

    private sealed record RecordedPkcs12Load(X509KeyStorageFlags Flags, X509Certificate2 Certificate);

    // Records the flags the installer asked for, then loads ephemerally so the real key never reaches the disk.
    private sealed class RecordingPkcs12Loader
    {
        public List<X509KeyStorageFlags> Attempts { get; } = [];

        public List<RecordedPkcs12Load> Loads { get; } = [];

        public X509Certificate2 Load(string path, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags)
        {
            Attempts.Add(keyStorageFlags);
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
            Loads.Add(new RecordedPkcs12Load(keyStorageFlags, certificate));
            return certificate;
        }
    }

    // Contains answers only from Present (seeded by a test), never from Added, so the in-process memory is observable.
    // Added keeps the instance the installer passed (to check it was disposed) and the thumbprint read while it was alive.
    private sealed class FakeCertificateStore : ICertificateStore
    {
        public HashSet<(StoreName StoreName, string Thumbprint)> Present { get; } = [];

        public List<(StoreName StoreName, string Thumbprint, X509Certificate2 Certificate)> Added { get; } = [];

        public int ContainsCalls { get; private set; }

        public bool Contains(StoreName storeName, string thumbprint)
        {
            ContainsCalls++;
            return Present.Contains((storeName, thumbprint));
        }

        public void Add(StoreName storeName, X509Certificate2 certificate) => Added.Add((storeName, certificate.Thumbprint, certificate));
    }
}

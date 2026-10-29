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
    [Fact]
    public void Install_Pfx_AddsToPersonalStoreOnce()
    {
        using var directory = new TemporaryDirectory();
        string pfxPath = CreateCertificateFiles(directory).PfxPath;
        var store = new FakeCertificateStore();
        CertificateInstaller installer = CreateInstaller(store);

        CertificateInstallResult first = installer.Install(pfxPath, "pfx-password");
        CertificateInstallResult second = installer.Install(pfxPath, "pfx-password");

        Assert.Equal(CertificateInstallStatus.Installed, first.Status);
        Assert.Equal(CertificateInstallStatus.AlreadyPresent, second.Status);
        (StoreName storeName, _, X509Certificate2 added) = Assert.Single(store.Added);
        Assert.Equal(StoreName.My, storeName);
        Assert.Equal(IntPtr.Zero, added.Handle);
    }

    [Fact]
    public void Install_PublicCertificate_AddsToRootStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new FakeCertificateStore();

        CertificateInstallResult result = CreateInstaller(store).Install(CreateCertificateFiles(directory).CerPath, ReadOnlySpan<char>.Empty);

        Assert.Equal(CertificateInstallStatus.Installed, result.Status);
        Assert.Equal(StoreName.Root, Assert.Single(store.Added).StoreName);
    }

    [Fact]
    public void Install_WrongPfxPassword_FailsWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RecordingLogger<CertificateInstaller>();
        var installer = new CertificateInstaller(new FakeCertificateStore(), logger, X509KeyStorageFlags.EphemeralKeySet);

        CertificateInstallResult result = installer.Install(CreateCertificateFiles(directory).PfxPath, "wrong-password");

        Assert.Equal(CertificateInstallStatus.Failed, result.Status);
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("client.pfx", warning.Properties["CertificateFileName"]);
        Assert.DoesNotContain("wrong-password", warning.Message, StringComparison.Ordinal);
    }

    private static CertificateInstaller CreateInstaller(FakeCertificateStore store) =>
        new(store, new RecordingLogger<CertificateInstaller>(), X509KeyStorageFlags.EphemeralKeySet);

    private static (string PfxPath, string CerPath) CreateCertificateFiles(TemporaryDirectory directory)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Foundry Connect Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (
            directory.CreateFile("client.pfx", certificate.Export(X509ContentType.Pfx, "pfx-password")),
            directory.CreateFile("root.cer", certificate.Export(X509ContentType.Cert)));
    }

    // Keeps the instance the installer passed (to check it was disposed) and the thumbprint read while it was alive.
    private sealed class FakeCertificateStore : ICertificateStore
    {
        public List<(StoreName StoreName, string Thumbprint, X509Certificate2 Certificate)> Added { get; } = [];

        public bool Contains(StoreName storeName, string thumbprint) =>
            Added.Any(item => item.StoreName == storeName && string.Equals(item.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));

        public void Add(StoreName storeName, X509Certificate2 certificate) => Added.Add((storeName, certificate.Thumbprint, certificate));
    }
}

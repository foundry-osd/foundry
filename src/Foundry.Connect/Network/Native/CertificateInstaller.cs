// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using Foundry.Core.Models.Network;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Outcome of one certificate import.
/// </summary>
public enum CertificateInstallStatus
{
    /// <summary>The certificate was added to the store.</summary>
    Installed,

    /// <summary>The store already holds a certificate with the same thumbprint; nothing was added.</summary>
    AlreadyPresent,

    /// <summary>The file could not be loaded or the store refused the certificate.</summary>
    Failed
}

/// <summary>
/// Result of <see cref="ICertificateInstaller.Install"/>.
/// </summary>
/// <param name="Status">The import outcome.</param>
/// <param name="Thumbprint">The certificate thumbprint, or <see langword="null"/> when the import failed.</param>
public sealed record CertificateInstallResult(CertificateInstallStatus Status, string? Thumbprint = null)
{
    /// <summary>Gets a value indicating whether the certificate is in the store after the call.</summary>
    public bool IsSuccess => Status is CertificateInstallStatus.Installed or CertificateInstallStatus.AlreadyPresent;
}

/// <summary>
/// The local machine certificate stores, behind an interface so tests never touch the real stores.
/// </summary>
public interface ICertificateStore
{
    /// <summary>Returns whether <paramref name="storeName"/> holds a certificate with <paramref name="thumbprint"/>.</summary>
    bool Contains(StoreName storeName, string thumbprint);

    /// <summary>Adds <paramref name="certificate"/> to <paramref name="storeName"/>; the store keeps its own copy.</summary>
    void Add(StoreName storeName, X509Certificate2 certificate);
}

/// <summary>
/// <see cref="ICertificateStore"/> over the <see cref="StoreLocation.LocalMachine"/> stores. Each call opens and closes its store.
/// </summary>
public sealed class LocalMachineCertificateStore : ICertificateStore
{
    /// <inheritdoc />
    public bool Contains(StoreName storeName, string thumbprint)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        // Every access to X509Store.Certificates and every Find result hands out instances the caller owns.
        X509Certificate2Collection certificates = store.Certificates;
        try
        {
            X509Certificate2Collection matches = certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            try
            {
                return matches.Count > 0;
            }
            finally
            {
                DisposeAll(matches);
            }
        }
        finally
        {
            DisposeAll(certificates);
        }
    }

    /// <inheritdoc />
    public void Add(StoreName storeName, X509Certificate2 certificate)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    private static void DisposeAll(X509Certificate2Collection certificates)
    {
        foreach (X509Certificate2 certificate in certificates)
        {
            certificate.Dispose();
        }
    }
}

/// <summary>
/// Imports a provisioned certificate file into the local machine stores.
/// </summary>
public interface ICertificateInstaller
{
    /// <summary>
    /// Imports <paramref name="certificatePath"/>: a <c>.pfx</c>/<c>.p12</c> file goes to <c>LocalMachine\My</c> (the password may
    /// be empty), any other file to <c>LocalMachine\Root</c>. Idempotent by thumbprint. Never throws; a failure is
    /// <see cref="CertificateInstallStatus.Failed"/>.
    /// </summary>
    CertificateInstallResult Install(string certificatePath, ReadOnlySpan<char> pfxPassword);

    /// <summary>
    /// Imports PKCS#12 content decrypted in memory (a client PFX staged encrypted, L4-5) into <c>LocalMachine\My</c>; the
    /// password may be empty. Idempotent by thumbprint. The caller keeps and zeroes both buffers. Never throws; a failure is
    /// <see cref="CertificateInstallStatus.Failed"/>.
    /// </summary>
    CertificateInstallResult InstallPfx(ReadOnlySpan<byte> pfxContent, ReadOnlySpan<char> password);
}

/// <summary>
/// Loads a PKCS#12 file; the seam lets tests observe which key storage flags each load uses.
/// </summary>
internal delegate X509Certificate2 Pkcs12FileLoader(string path, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags);

/// <summary>
/// Loads PKCS#12 content from memory; the seam lets tests observe which key storage flags each load uses.
/// </summary>
internal delegate X509Certificate2 Pkcs12ContentLoader(ReadOnlySpan<byte> content, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags);

/// <summary>
/// <see cref="ICertificateInstaller"/> that loads with <see cref="X509CertificateLoader"/>, adds a certificate once per process
/// and per store (T10), and disposes every loaded instance.
/// </summary>
/// <remarks>
/// A PFX, from a file or from decrypted content, is first loaded with <see cref="X509KeyStorageFlags.EphemeralKeySet"/> only to
/// read its thumbprint, so a certificate that is already installed never writes a private key. Only when the store needs it is it loaded again with the
/// persisting flags: <see cref="X509KeyStorageFlags.PersistKeySet"/> keys stay on disk after dispose, in a new container per
/// load, so a persisting load that is not added would leave an orphaned machine key. The store keeps its own copy, so every
/// loaded instance is disposed. Residual risk: when the store refuses the certificate after the persisting load, that key
/// stays on disk; it is accepted because the WinPE system drive (X:) is volatile and is discarded at reboot. A failure
/// is logged with the certificate kind and the exception type only: PKCS#12 error messages can echo their inputs, and
/// author file names can carry organization names into the remote Logs stream.
/// </remarks>
public sealed class CertificateInstaller : ICertificateInstaller
{
    private const X509KeyStorageFlags ProductionPfxKeyStorageFlags = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet;
    private const string PfxCertificateKind = "pfx";

    private readonly object _gate = new();
    private readonly HashSet<(StoreName StoreName, string Thumbprint)> _installed = [];
    private readonly ICertificateStore _store;
    private readonly ILogger<CertificateInstaller> _logger;
    private readonly X509KeyStorageFlags _pfxKeyStorageFlags;
    private readonly Pkcs12FileLoader _loadPkcs12;
    private readonly Pkcs12ContentLoader _loadPkcs12Content;

    /// <summary>
    /// Initializes an installer that persists PFX private keys in the machine key set.
    /// </summary>
    public CertificateInstaller(ICertificateStore store, ILogger<CertificateInstaller> logger)
        : this(store, logger, ProductionPfxKeyStorageFlags)
    {
    }

    /// <summary>
    /// Lets tests choose the flags of the persisting load (<see cref="X509KeyStorageFlags.EphemeralKeySet"/> keeps keys off the disk)
    /// and observe every PKCS#12 load.
    /// </summary>
    internal CertificateInstaller(
        ICertificateStore store,
        ILogger<CertificateInstaller> logger,
        X509KeyStorageFlags pfxKeyStorageFlags,
        Pkcs12FileLoader? loadPkcs12 = null,
        Pkcs12ContentLoader? loadPkcs12Content = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pfxKeyStorageFlags = pfxKeyStorageFlags;
        _loadPkcs12 = loadPkcs12 ?? (static (path, password, flags) => X509CertificateLoader.LoadPkcs12FromFile(path, password, flags));
        _loadPkcs12Content = loadPkcs12Content ?? (static (content, password, flags) => X509CertificateLoader.LoadPkcs12(content, password, flags));
    }

    /// <inheritdoc />
    public CertificateInstallResult Install(string certificatePath, ReadOnlySpan<char> pfxPassword)
    {
        bool isPfx = NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath);
        try
        {
            return isPfx ? InstallPfx(certificatePath, pfxPassword) : InstallPublicCertificate(certificatePath);
        }
        catch (Exception exception)
        {
            return LogFailure(GetCertificateKind(certificatePath), exception);
        }
    }

    /// <inheritdoc />
    public CertificateInstallResult InstallPfx(ReadOnlySpan<byte> pfxContent, ReadOnlySpan<char> password)
    {
        try
        {
            return InstallPfxContent(pfxContent, password);
        }
        catch (Exception exception)
        {
            return LogFailure(PfxCertificateKind, exception);
        }
    }

    /// <summary>Returns the non-identifying kind logged instead of the author file name: <c>pfx</c> or <c>certificate</c>.</summary>
    internal static string GetCertificateKind(string certificatePath) => NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath) ? PfxCertificateKind : "certificate";

    private CertificateInstallResult LogFailure(string certificateKind, Exception exception)
    {
        _logger.LogWarning(
            "Certificate import failed. CertificateKind={CertificateKind}, FailureType={FailureType}",
            certificateKind,
            exception.GetType().Name);
        return new CertificateInstallResult(CertificateInstallStatus.Failed);
    }

    private CertificateInstallResult InstallPfx(string certificatePath, ReadOnlySpan<char> pfxPassword)
    {
        string thumbprint;
        using (X509Certificate2 probe = _loadPkcs12(certificatePath, pfxPassword, X509KeyStorageFlags.EphemeralKeySet))
        {
            thumbprint = probe.Thumbprint;
        }

        lock (_gate)
        {
            if (IsAlreadyPresent(StoreName.My, thumbprint))
            {
                return new CertificateInstallResult(CertificateInstallStatus.AlreadyPresent, thumbprint);
            }

            using X509Certificate2 certificate = _loadPkcs12(certificatePath, pfxPassword, _pfxKeyStorageFlags);
            return Add(StoreName.My, certificate, thumbprint);
        }
    }

    // The same probe-then-persist rule as InstallPfx(string, ...), over in-memory content.
    private CertificateInstallResult InstallPfxContent(ReadOnlySpan<byte> pfxContent, ReadOnlySpan<char> password)
    {
        string thumbprint;
        using (X509Certificate2 probe = _loadPkcs12Content(pfxContent, password, X509KeyStorageFlags.EphemeralKeySet))
        {
            thumbprint = probe.Thumbprint;
        }

        lock (_gate)
        {
            if (IsAlreadyPresent(StoreName.My, thumbprint))
            {
                return new CertificateInstallResult(CertificateInstallStatus.AlreadyPresent, thumbprint);
            }

            using X509Certificate2 certificate = _loadPkcs12Content(pfxContent, password, _pfxKeyStorageFlags);
            return Add(StoreName.My, certificate, thumbprint);
        }
    }

    private CertificateInstallResult InstallPublicCertificate(string certificatePath)
    {
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        string thumbprint = certificate.Thumbprint;
        lock (_gate)
        {
            return IsAlreadyPresent(StoreName.Root, thumbprint)
                ? new CertificateInstallResult(CertificateInstallStatus.AlreadyPresent, thumbprint)
                : Add(StoreName.Root, certificate, thumbprint);
        }
    }

    // Callers hold _gate.
    private bool IsAlreadyPresent(StoreName storeName, string thumbprint)
    {
        if (_installed.Contains((storeName, thumbprint)))
        {
            return true;
        }

        if (!_store.Contains(storeName, thumbprint))
        {
            return false;
        }

        _installed.Add((storeName, thumbprint));
        return true;
    }

    // Callers hold _gate.
    private CertificateInstallResult Add(StoreName storeName, X509Certificate2 certificate, string thumbprint)
    {
        _store.Add(storeName, certificate);
        _installed.Add((storeName, thumbprint));
        return new CertificateInstallResult(CertificateInstallStatus.Installed, thumbprint);
    }
}

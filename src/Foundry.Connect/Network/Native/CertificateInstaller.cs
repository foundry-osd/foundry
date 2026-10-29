// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography.X509Certificates;
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
}

/// <summary>
/// <see cref="ICertificateInstaller"/> that loads with <see cref="X509CertificateLoader"/>, adds a certificate once per process
/// and per store (T10), and disposes every loaded instance.
/// </summary>
/// <remarks>
/// The store keeps its own copy, so the loaded instance is disposed right after <see cref="ICertificateStore.Add"/>. With
/// <see cref="X509KeyStorageFlags.PersistKeySet"/> the private key stays on disk after that dispose. A failure is logged with
/// the file name and the exception type only: PKCS#12 error messages can echo their inputs.
/// </remarks>
public sealed class CertificateInstaller : ICertificateInstaller
{
    private const X509KeyStorageFlags ProductionPfxKeyStorageFlags = X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet;

    private readonly object _gate = new();
    private readonly HashSet<(StoreName StoreName, string Thumbprint)> _installed = [];
    private readonly ICertificateStore _store;
    private readonly ILogger<CertificateInstaller> _logger;
    private readonly X509KeyStorageFlags _pfxKeyStorageFlags;

    /// <summary>
    /// Initializes an installer that persists PFX private keys in the machine key set.
    /// </summary>
    public CertificateInstaller(ICertificateStore store, ILogger<CertificateInstaller> logger)
        : this(store, logger, ProductionPfxKeyStorageFlags)
    {
    }

    /// <summary>Lets tests load PFX files with <see cref="X509KeyStorageFlags.EphemeralKeySet"/> so no key reaches the disk.</summary>
    internal CertificateInstaller(ICertificateStore store, ILogger<CertificateInstaller> logger, X509KeyStorageFlags pfxKeyStorageFlags)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pfxKeyStorageFlags = pfxKeyStorageFlags;
    }

    /// <inheritdoc />
    public CertificateInstallResult Install(string certificatePath, ReadOnlySpan<char> pfxPassword)
    {
        X509Certificate2? certificate = null;
        try
        {
            bool isPfx = IsPfxPath(certificatePath);
            certificate = isPfx
                ? X509CertificateLoader.LoadPkcs12FromFile(certificatePath, pfxPassword, _pfxKeyStorageFlags)
                : X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            StoreName storeName = isPfx ? StoreName.My : StoreName.Root;
            string thumbprint = certificate.Thumbprint;

            lock (_gate)
            {
                if (_installed.Contains((storeName, thumbprint)) || _store.Contains(storeName, thumbprint))
                {
                    _installed.Add((storeName, thumbprint));
                    return new CertificateInstallResult(CertificateInstallStatus.AlreadyPresent, thumbprint);
                }

                _store.Add(storeName, certificate);
                _installed.Add((storeName, thumbprint));
                return new CertificateInstallResult(CertificateInstallStatus.Installed, thumbprint);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Certificate import failed. CertificateFileName={CertificateFileName}, FailureType={FailureType}",
                Path.GetFileName(certificatePath),
                exception.GetType().Name);
            return new CertificateInstallResult(CertificateInstallStatus.Failed);
        }
        finally
        {
            certificate?.Dispose();
        }
    }

    private static bool IsPfxPath(string certificatePath)
    {
        string extension = Path.GetExtension(certificatePath);
        return string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase);
    }
}

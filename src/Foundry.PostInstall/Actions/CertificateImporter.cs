// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text;

namespace Foundry.PostInstall.Actions;

public interface ICertificateImporter
{
    void Import(string path, string kind, string store, string? passwordPath);
}

public sealed class CertificateImporter : ICertificateImporter
{
    public void Import(string path, string kind, string store, string? passwordPath)
    {
        if (kind.Equals("pfx", StringComparison.OrdinalIgnoreCase))
        {
            byte[] bytes = passwordPath is null ? [] : File.ReadAllBytes(passwordPath);
            char[] password = Encoding.UTF8.GetChars(bytes);
            X509Certificate2Collection? certificates = null;
            try
            {
                certificates = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
                using var target = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                target.Open(OpenFlags.ReadWrite);
                foreach (X509Certificate2 certificate in certificates) target.Add(certificate);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
                if (certificates is not null) foreach (X509Certificate2 certificate in certificates) certificate.Dispose();
            }
            return;
        }
        if (store is not ("Root" or "CA" or "My" or "TrustedPeople" or "TrustedPublisher" or "AuthRoot"))
            throw new InvalidDataException("Unsupported machine certificate store.");
        using X509Certificate2 publicCertificate = X509CertificateLoader.LoadCertificateFromFile(path);
        using var publicStore = new X509Store(store, StoreLocation.LocalMachine);
        publicStore.Open(OpenFlags.ReadWrite);
        publicStore.Add(publicCertificate);
    }
}

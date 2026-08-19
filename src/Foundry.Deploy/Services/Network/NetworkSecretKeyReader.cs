// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Utilities.Security;

namespace Foundry.Deploy.Services.Network;

public interface INetworkSecretKeyReader
{
    Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the Foundry Connect network key from the generated boot media.
/// </summary>
public sealed class NetworkSecretKeyReader : INetworkSecretKeyReader
{
    private const string KeyRelativePath = @"Config\Secrets\media-secrets.key";

    public async Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);

        string keyPath = Path.Combine(workspaceRootPath, KeyRelativePath);
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException("Network secret key was not found in the boot media configuration.", keyPath);
        }

        byte[] key = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
        if (key.Length != AesGcmEncryption.KeySizeBytes)
        {
            throw new InvalidOperationException("Network secret key has an invalid length.");
        }

        return key;
    }
}

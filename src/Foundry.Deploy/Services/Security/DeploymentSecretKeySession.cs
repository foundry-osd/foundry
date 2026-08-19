// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentSecretKeySession : IDeploymentSecretKeySession, IDisposable
{
    private byte[]? key;
    private bool isDisposed;

    public bool IsUnlocked => !isDisposed && key is { Length: 32 };

    public void SetKey(ReadOnlySpan<byte> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (value.Length != 32)
        {
            throw new ArgumentException("Deploy secret key must be 32 bytes.", nameof(value));
        }

        ClearKey();
        key = value.ToArray();
    }

    public byte[] GetKeyCopy()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return key?.ToArray() ?? throw new InvalidOperationException("Deployment secrets are not unlocked.");
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ClearKey();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        ClearKey();
        isDisposed = true;
    }

    private void ClearKey()
    {
        if (key is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(key);
        key = null;
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Security;

public interface IDeploymentSecretKeySession : IDisposable
{
    bool IsUnlocked { get; }

    void SetKey(ReadOnlySpan<byte> key);

    byte[] GetKeyCopy();

    void Clear();
}

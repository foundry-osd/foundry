// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Services.Configuration;

internal sealed class NetworkCacheSecretStateService : INetworkCacheSecretStateService, IDisposable
{
    private readonly NetworkCacheSecretState state = new();
    public event EventHandler? Changed;
    public void SetPassword(string accountId, ReadOnlySpan<char> value)
    {
        state.SetPassword(accountId, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public char[]? GetPasswordCopy(string accountId) => state.GetPasswordCopy(accountId);
    public bool HasPassword(string accountId) => state.HasPassword(accountId);
    public void Update(NetworkCacheSettings settings)
    {
        if (state.Update(settings)) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => state.Dispose();
}

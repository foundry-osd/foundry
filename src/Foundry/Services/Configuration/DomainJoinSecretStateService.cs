// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Owns the desktop session's join account passwords and publishes changes to profile persistence.</summary>
internal sealed class DomainJoinSecretStateService : IDomainJoinSecretStateService, IDisposable
{
    private readonly DomainJoinSecretState state = new();
    public event EventHandler? Changed;
    public void SetPassword(string accountName, ReadOnlySpan<char> value)
    {
        state.SetPassword(accountName, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public char[]? GetPasswordCopy(string accountName) => state.GetPasswordCopy(accountName);
    public bool HasPassword(string accountName) => state.HasPassword(accountName);
    public void Update(DomainJoinSettings settings)
    {
        if (state.Update(settings)) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear()
    {
        if (state.Clear()) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => state.Dispose();
}

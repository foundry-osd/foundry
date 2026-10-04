// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Owns the desktop session's domain password and publishes changes to profile persistence.</summary>
internal sealed class DomainJoinSecretStateService : IDomainJoinSecretStateService, IDisposable
{
    private readonly DomainJoinSecretState state = new();
    public event EventHandler? Changed;
    public void SetPassword(DomainJoinCredentialContext context, ReadOnlySpan<char> value)
    {
        state.SetPassword(context, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public char[]? GetPasswordCopy(DomainJoinCredentialContext context) => state.GetPasswordCopy(context);
    public bool HasPassword(DomainJoinCredentialContext context) => state.HasPassword(context);
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

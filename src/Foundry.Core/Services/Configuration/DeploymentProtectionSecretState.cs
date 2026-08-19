// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Owns deployment media password values for the current Foundry OSD process only.
/// </summary>
public sealed class DeploymentProtectionSecretState : IDisposable
{
    private char[]? password;
    private char[]? confirmation;
    private bool isDisposed;

    public bool HasPassword => password is { Length: > 0 };

    public bool HasConfirmation => confirmation is { Length: > 0 };

    public bool IsValid =>
        password is { Length: >= DeploymentProtectionPasswordRules.MinimumLength } &&
        confirmation is not null &&
        password.AsSpan().SequenceEqual(confirmation);

    public bool ShouldRecommendStrongerPassword =>
        IsValid && password!.Length < DeploymentProtectionPasswordRules.RecommendedLength;

    public void SetPassword(ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Replace(ref password, value);
    }

    public void SetConfirmation(ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Replace(ref confirmation, value);
    }

    public char[] GetConfirmedPasswordCopy()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!IsValid)
        {
            throw new InvalidOperationException("A valid confirmed deployment media password is not available.");
        }

        return password!.ToArray();
    }

    public void Clear()
    {
        ClearBuffer(ref password);
        ClearBuffer(ref confirmation);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Clear();
        isDisposed = true;
    }

    private static void Replace(ref char[]? target, ReadOnlySpan<char> value)
    {
        ClearBuffer(ref target);
        target = value.IsEmpty ? null : value.ToArray();
    }

    private static void ClearBuffer(ref char[]? value)
    {
        if (value is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        value = null;
    }
}

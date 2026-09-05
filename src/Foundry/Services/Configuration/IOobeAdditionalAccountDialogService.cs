// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IOobeAdditionalAccountDialogService
{
    Task<OobeAdditionalAccountDialogResult?> ShowAsync(
        OobeAdditionalAccountSettings? account,
        IReadOnlyList<OobeAdditionalAccountSettings> existingAccounts,
        char[] initialPassword,
        char[] initialConfirmation);
}

public sealed partial class OobeAdditionalAccountDialogResult : IDisposable
{
    private bool isDisposed;

    public OobeAdditionalAccountDialogResult(
        OobeAdditionalAccountSettings account,
        char[] password,
        char[] confirmation)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Password = password ?? throw new ArgumentNullException(nameof(password));
        Confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
    }

    public OobeAdditionalAccountSettings Account { get; }

    public char[] Password { get; }

    public char[] Confirmation { get; }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(Password.AsSpan()));
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(Confirmation.AsSpan()));
        isDisposed = true;
    }
}

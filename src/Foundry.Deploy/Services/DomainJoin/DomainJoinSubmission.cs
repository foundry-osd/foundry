// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>
/// Carries what the technician entered on the Domain Join wizard step. It owns a transient copy of the password,
/// and no diagnostic string is ever built from it.
/// </summary>
public sealed class DomainJoinSubmission : IDisposable
{
    private readonly char[] password;

    public DomainJoinSubmission(string domainName, string accountName, string? selectedOuId, ReadOnlySpan<char> password,
        string? typedOuDistinguishedName = null)
    {
        DomainName = domainName;
        AccountName = accountName;
        SelectedOuId = selectedOuId;
        TypedOuDistinguishedName = typedOuDistinguishedName;
        this.password = password.ToArray();
    }

    internal string DomainName { get; }
    internal string AccountName { get; }
    internal string? SelectedOuId { get; }
    /// <summary>Optional OU typed in Interactive mode, accepted only when no saved OU list applies to the domain.</summary>
    internal string? TypedOuDistinguishedName { get; }
    internal ReadOnlyMemory<char> Password => password;

    public void Dispose() => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
}

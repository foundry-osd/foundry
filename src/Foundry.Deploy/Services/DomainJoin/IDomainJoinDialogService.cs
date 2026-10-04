// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Collects interactive credentials or an automatic-mode destination before confirmation.</summary>
public interface IDomainJoinDialogService
{
    /// <summary>Returns null for cancellation; the caller disposes any submitted password copy.</summary>
    DomainJoinDialogResult? Show(DeployDomainJoinSettings settings, bool requiresCredentials);
}

/// <summary>Owns submitted transient password characters; no synthesized diagnostic string includes them.</summary>
public sealed class DomainJoinDialogResult : IDisposable
{
    private readonly char[] password;
    public DomainJoinDialogResult(string domainName, string accountName, string? selectedOuId, ReadOnlySpan<char> password,
        string? destinationDn = null)
    {
        DomainName = domainName;
        AccountName = accountName;
        SelectedOuId = selectedOuId;
        DestinationDn = destinationDn;
        this.password = password.ToArray();
    }
    internal string DomainName { get; }
    internal string AccountName { get; }
    internal string? SelectedOuId { get; }
    /// <summary>Optional interactive destination, accepted only without a compatible authored catalog.</summary>
    internal string? DestinationDn { get; }
    internal ReadOnlyMemory<char> Password => password;
    public void Dispose() => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
}

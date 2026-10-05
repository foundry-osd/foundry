// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
namespace Foundry.PostInstall.Windows;

/// <summary>Owns a signed/sealed connection to one writable DC for serialized reads and at most one move.</summary>
internal interface IDomainComputerAccountDirectory : IDisposable
{
    Task<DomainDirectoryReady> PrepareAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password,
        string computerName, string? targetOuDn, CancellationToken token);
    Task<DomainDirectoryObject?> FindComputerAsync(string computerName, CancellationToken token);
    Task<DomainDirectoryObject> ReadComputerAsync(Guid guid, CancellationToken token);
    Task<DomainDirectoryObject> ReadDestinationAsync(Guid guid, CancellationToken token);
    Task MoveAsync(DomainDirectoryObject computer, DomainDirectoryObject destination, CancellationToken token);
}
/// <summary>Directory identity captured on the chosen DC, including the live parent's identity for computers.</summary>
internal sealed record DomainDirectoryObject(Guid Guid, string DistinguishedName, Guid? ParentGuid = null);
/// <summary>
/// A completed readiness read; null computer means no visible match, never proof of absolute absence.
/// DestinationMissing means the directory answered that the requested OU does not exist or is not an
/// organizational unit, so the join can still proceed in the domain's default location.
/// </summary>
internal sealed record DomainDirectoryReady(string Domain, string Controller, DomainDirectoryObject? Destination, DomainDirectoryObject? Computer,
    bool DestinationMissing = false);
/// <summary>
/// Sanitized numeric diagnostics; Rejected indicates an acknowledged server rejection of a write, and Transient a
/// readiness failure that happened before the supplied identity was evaluated: the controller could not be located
/// or connected to. A bind that timed out is not transient, because the controller may have seen the credentials.
/// </summary>
internal sealed class DomainDirectoryException(int? nativeError = null, int? ldapError = null, bool rejected = false, int? directoryResult = null,
    bool transient = false) : Exception("Directory operation unavailable.")
{
    public bool Transient { get; } = transient;
    public int? NativeErrorCode { get; } = nativeError;
    public int? LdapErrorCode { get; } = ldapError;
    public bool Rejected { get; } = rejected;
    public int? DirectoryResultCode { get; } = directoryResult;
}

/// <summary>Authenticates to one writable DC per readiness attempt and preserves GUID, naming-context, and RDN boundaries for relocation.</summary>
internal sealed class DomainComputerAccountDirectory : IDomainComputerAccountDirectory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const int LdapServerDown = 81;
    private const int LdapConnectError = 91;
    private const int NoSuchObject = 32;
    private const uint ForceRediscovery = 0x1;
    private LdapConnection? connection;
    private string domain = string.Empty;
    private string namingContext = string.Empty;
    private string computerName = string.Empty;
    private bool moveStarted;
    private bool readinessAttempted;

    /// <summary>Leaves no connection behind on failure, so a transient readiness failure can be attempted again.</summary>
    public async Task<DomainDirectoryReady> PrepareAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password,
        string computerName, string? targetOuDn, CancellationToken token)
    {
        if (connection is not null) throw new InvalidOperationException("Directory readiness cannot be repeated.");
        try
        {
            return await ConnectAsync(context, password, computerName, targetOuDn, token).ConfigureAwait(false);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            readinessAttempted = true;
        }
    }

    private async Task<DomainDirectoryReady> ConnectAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password,
        string computerName, string? targetOuDn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        domain = DomainJoinCredentialContext.CanonicalizeDomainName(context.DomainName);
        this.computerName = computerName;
        // A repeated attempt bypasses the locator's cached failure from the previous one.
        string controller = LocateController(domain, forceRediscovery: readinessAttempted);
        token.ThrowIfCancellationRequested();
        using var secret = new SecureString();
        foreach (char character in password.Span) secret.AppendChar(character);
        secret.MakeReadOnly();
        string[] account = context.AccountName.Trim().Split('\\');
        var credential = account.Length == 2 ? new NetworkCredential(account[1], secret, account[0]) : new NetworkCredential(account[0], secret);
        try
        {
            connection = new LdapConnection(new LdapDirectoryIdentifier(controller, 389, true, false), credential, AuthType.Negotiate);
            connection.Timeout = RequestTimeout;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.AutoReconnect = false;
            connection.SessionOptions.SendTimeout = RequestTimeout;
            connection.Bind();
            token.ThrowIfCancellationRequested();
        }
        catch (LdapException error)
        {
            throw new DomainDirectoryException(ldapError: error.ErrorCode,
                transient: error.ErrorCode is LdapServerDown or LdapConnectError);
        }
        finally { credential.Password = null; }
        var root = await SearchAsync("", "(objectClass=*)", SearchScope.Base, ["defaultNamingContext", "dnsHostName"], token).ConfigureAwait(false);
        if (root.Count != 1 || !string.Equals(Attribute(root[0], "dnsHostName"), controller, StringComparison.OrdinalIgnoreCase)) throw new DomainDirectoryException();
        namingContext = Attribute(root[0], "defaultNamingContext");
        if (!DistinguishedNameRules.TryParse(namingContext, out var parsed) || parsed.Rdns.Count != domain.Split('.').Length ||
            !DistinguishedNameRules.IsWithinDomain(namingContext, domain)) throw new DomainDirectoryException();
        if (targetOuDn is null) return new(domain, controller, null, null);
        if (!DistinguishedNameRules.IsWithinDomain(targetOuDn, domain)) throw new DomainDirectoryException();
        var target = await FindOrganizationalUnitAsync(targetOuDn, token).ConfigureAwait(false);
        if (target is null) return new(domain, controller, null, null, DestinationMissing: true);
        return new(domain, controller, target, await FindComputerAsync(computerName, token).ConfigureAwait(false));
    }

    /// <summary>Returns null only when the directory positively answers that no such organizational unit exists.</summary>
    private async Task<DomainDirectoryObject?> FindOrganizationalUnitAsync(string dn, CancellationToken token)
    {
        try
        {
            var results = await SearchAsync(dn, "(objectClass=organizationalUnit)", SearchScope.Base, ["distinguishedName", "objectGUID"], token).ConfigureAwait(false);
            if (results.Count > 1) throw new DomainDirectoryException();
            return results.Count == 0 ? null : Parse(results[0]);
        }
        catch (DomainDirectoryException error) when (error.DirectoryResultCode == NoSuchObject)
        {
            return null;
        }
    }

    public async Task<DomainDirectoryObject?> FindComputerAsync(string name, CancellationToken token)
    {
        var results = await SearchAsync(namingContext, "(&(objectCategory=computer)(sAMAccountName=" + EscapeFilter(name + "$") + "))",
            SearchScope.Subtree, ["distinguishedName", "objectGUID"], token).ConfigureAwait(false);
        if (results.Count == 0) return null;
        if (results.Count != 1) throw new DomainDirectoryException();
        return await WithParentAsync(Parse(results[0]), token).ConfigureAwait(false);
    }
    public async Task<DomainDirectoryObject> ReadComputerAsync(Guid guid, CancellationToken token)
    {
        var value = await ReadGuidAsync(guid, "computer", token).ConfigureAwait(false);
        return await WithParentAsync(value, token).ConfigureAwait(false);
    }
    public Task<DomainDirectoryObject> ReadDestinationAsync(Guid guid, CancellationToken token) => ReadGuidAsync(guid, "organizationalUnit", token);

    public async Task MoveAsync(DomainDirectoryObject computer, DomainDirectoryObject destination, CancellationToken token)
    {
        if (moveStarted) throw new InvalidOperationException("A directory mutation cannot be repeated.");
        var liveComputer = await ReadComputerAsync(computer.Guid, token).ConfigureAwait(false);
        var namedComputer = await FindComputerAsync(computerName, token).ConfigureAwait(false);
        var liveTarget = await ReadDestinationAsync(destination.Guid, token).ConfigureAwait(false);
        if (namedComputer?.Guid != computer.Guid || liveComputer.Guid != computer.Guid || liveTarget.Guid != destination.Guid ||
            liveComputer.DistinguishedName != computer.DistinguishedName || liveTarget.DistinguishedName != destination.DistinguishedName)
            throw new DomainDirectoryException(rejected: true);
        if (liveComputer.ParentGuid == liveTarget.Guid) return;
        var request = CreateMoveRequest(liveComputer.DistinguishedName, liveTarget.DistinguishedName);
        token.ThrowIfCancellationRequested();
        moveStarted = true;
        await SendAsync(request, token).ConfigureAwait(false);
    }

    /// <summary>Constructs the move after caller identity checks, preserving the complete original escaped RDN.</summary>
    internal static ModifyDNRequest CreateMoveRequest(string sourceDn, string destinationDn)
    {
        if (!DistinguishedNameRules.TryParse(sourceDn, out var parsed) || parsed.Parent is null) throw new DomainDirectoryException(rejected: true);
        string rdn = sourceDn[..^(parsed.Parent.Length + 1)];
        return new ModifyDNRequest(sourceDn, destinationDn, rdn) { DeleteOldRdn = true };
    }

    private async Task<DomainDirectoryObject> ReadGuidAsync(Guid guid, string objectClass, CancellationToken token)
    {
        string binary = string.Concat(guid.ToByteArray().Select(value => "\\" + value.ToString("x2")));
        var results = await SearchAsync(namingContext, $"(&(objectClass={objectClass})(objectGUID={binary}))", SearchScope.Subtree,
            ["distinguishedName", "objectGUID"], token).ConfigureAwait(false);
        if (results.Count != 1) throw new DomainDirectoryException();
        var value = Parse(results[0]);
        if (value.Guid != guid) throw new DomainDirectoryException();
        return value;
    }
    private async Task<DomainDirectoryObject> ReadBaseAsync(string dn, string filter, CancellationToken token)
    {
        var results = await SearchAsync(dn, filter, SearchScope.Base, ["distinguishedName", "objectGUID"], token).ConfigureAwait(false);
        if (results.Count != 1) throw new DomainDirectoryException();
        return Parse(results[0]);
    }
    private async Task<DomainDirectoryObject> WithParentAsync(DomainDirectoryObject value, CancellationToken token)
    {
        if (!DistinguishedNameRules.TryParse(value.DistinguishedName, out var parsed) || parsed.Parent is null) throw new DomainDirectoryException();
        var parent = await ReadBaseAsync(parsed.Parent, "(objectClass=*)", token).ConfigureAwait(false);
        return value with { ParentGuid = parent.Guid };
    }
    private DomainDirectoryObject Parse(SearchResultEntry entry)
    {
        string dn = Attribute(entry, "distinguishedName");
        if (!DistinguishedNameRules.IsWithinDomain(dn, domain) || entry.Attributes["objectGUID"] is not { Count: 1 } attribute ||
            attribute[0] is not byte[] { Length: 16 } guid || new Guid(guid) == Guid.Empty) throw new DomainDirectoryException();
        return new(new Guid(guid), dn);
    }
    private async Task<List<SearchResultEntry>> SearchAsync(string dn, string filter, SearchScope scope, string[] attributes, CancellationToken token)
    {
        var request = new SearchRequest(dn, filter, scope, attributes) { TimeLimit = RequestTimeout };
        var paging = new PageResultRequestControl(256) { IsCritical = true };
        if (scope == SearchScope.Subtree)
        {
            request.Controls.Add(new DomainScopeControl { IsCritical = true }); request.Controls.Add(paging);
        }
        List<SearchResultEntry> entries = [];
        do
        {
            var response = (SearchResponse)await SendAsync(request, token).ConfigureAwait(false);
            if (response.ResultCode != ResultCode.Success || response.References.Count > 0 || response.Referral.Length > 0) throw new DomainDirectoryException();
            foreach (SearchResultEntry entry in response.Entries)
            {
                if (entries.Count >= 4096) throw new DomainDirectoryException();
                entries.Add(entry);
            }
            if (scope != SearchScope.Subtree) return entries;
            var controls = response.Controls.OfType<PageResultResponseControl>().ToArray();
            if (controls.Length != 1) throw new DomainDirectoryException();
            paging.Cookie = controls[0].Cookie;
            if (paging.Cookie.Length > 0 && entries.Count >= 4096) throw new DomainDirectoryException();
        } while (paging.Cookie.Length != 0);
        return entries;
    }
    private async Task<DirectoryResponse> SendAsync(DirectoryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owned = connection ?? throw new InvalidOperationException("Directory readiness is required.");
        var completion = new TaskCompletionSource<DirectoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object(); bool settled = false;
        try
        {
            // Begin may connect/send synchronously. Retain ownership until it returns and cancellation settles.
            IAsyncResult pending = owned.BeginSendRequest(request, RequestTimeout, PartialResultProcessing.NoPartialResultSupport, result =>
            {
                lock (sync)
                {
                    if (settled) return; settled = true;
                    try { completion.TrySetResult(owned.EndSendRequest(result)); }
                    catch (Exception error) { completion.TrySetException(error); }
                }
            }, null);
            using var registration = token.Register(() =>
            {
                lock (sync)
                {
                    if (settled) return; settled = true;
                    try { owned.Abort(pending); }
                    catch (ArgumentException) { }
                    finally { completion.TrySetCanceled(token); }
                }
            });
            return await completion.Task.ConfigureAwait(false);
        }
        catch (LdapException error) { throw new DomainDirectoryException(ldapError: error.ErrorCode); }
        catch (DirectoryOperationException error)
        {
            throw new DomainDirectoryException(directoryResult: error.Response is null ? null : (int)error.Response.ResultCode,
                rejected: error.Response is not null && error.Response.ResultCode != ResultCode.Success);
        }
    }
    internal static string EscapeFilter(string value) => value.Replace("\\", "\\5c", StringComparison.Ordinal).Replace("*", "\\2a", StringComparison.Ordinal)
        .Replace("(", "\\28", StringComparison.Ordinal).Replace(")", "\\29", StringComparison.Ordinal).Replace("\0", "\\00", StringComparison.Ordinal);
    private static string Attribute(SearchResultEntry entry, string name) => entry.Attributes[name] is { Count: 1 } attribute && attribute[0] is string value ? value : string.Empty;
    public void Dispose() { connection?.Dispose(); connection = null; }
    private static string LocateController(string domain, bool forceRediscovery)
    {
        nint buffer = 0;
        try
        {
            uint status = DsGetDcName(null, domain, 0, null,
                0x10 | 0x20000 | 0x40000000 | 0x1000 | (forceRediscovery ? ForceRediscovery : 0), out buffer);
            if (status != 0) throw new DomainDirectoryException(nativeError: unchecked((int)status), transient: true);
            var info = Marshal.PtrToStructure<DomainControllerInfo>(buffer);
            string foundDomain = Marshal.PtrToStringUni(info.DomainName) ?? string.Empty;
            string controller = (Marshal.PtrToStringUni(info.DomainControllerName) ?? string.Empty).TrimStart('\\');
            if (DomainJoinCredentialContext.CanonicalizeDomainName(foundDomain) != domain || !DomainJoinCredentialContext.IsValidDomainName(controller)) throw new DomainDirectoryException();
            return controller;
        }
        finally { if (buffer != 0) NetApiBufferFree(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DomainControllerInfo
    {
        public nint DomainControllerName; public nint DomainControllerAddress; public uint DomainControllerAddressType;
        public Guid DomainGuid; public nint DomainName; public nint DnsForestName; public uint Flags; public nint DcSiteName; public nint ClientSiteName;
    }
    [DllImport("netapi32.dll", EntryPoint = "DsGetDcNameW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint DsGetDcName(string? computerName, string? domainName, nint domainGuid, string? siteName, uint flags, out nint buffer);
    [DllImport("netapi32.dll", ExactSpelling = true)]
    private static extern uint NetApiBufferFree(nint buffer);
}

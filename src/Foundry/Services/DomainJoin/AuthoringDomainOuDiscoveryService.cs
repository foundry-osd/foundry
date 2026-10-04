// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.DirectoryServices.Protocols;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Serilog;

namespace Foundry.Services.DomainJoin;

/// <summary>Owns serialized native locator/bind work until it drains, including after the UI stops waiting.</summary>
internal sealed class AuthoringDomainOuDiscoveryService : IAuthoringDomainOuDiscoveryService
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly ILogger logger;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public AuthoringDomainOuDiscoveryService(ILogger logger) => this.logger = logger.ForContext<AuthoringDomainOuDiscoveryService>();

    /// <inheritdoc />
    public async Task<DomainOuDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        logger.Information("Domain destination discovery started.");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        Task<DomainOuDiscoveryResult> work = Task.Run(async () =>
        {
            DomainOuDiscoveryResult? result = null;
            try
            {
                await operationGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    result = await DiscoverOwnedAsync(lifetime.Token).ConfigureAwait(false);
                    if (result.Status == DomainOuDiscoveryStatus.Canceled && !cancellationToken.IsCancellationRequested)
                        result = result with { Status = DomainOuDiscoveryStatus.Unavailable, ErrorCode = "Timeout" };
                    return result;
                }
                finally { operationGate.Release(); }
            }
            catch (OperationCanceledException)
            {
                return result = new(null, null, [], cancellationToken.IsCancellationRequested ? DomainOuDiscoveryStatus.Canceled : DomainOuDiscoveryStatus.Unavailable,
                    cancellationToken.IsCancellationRequested ? "Canceled" : "Timeout");
            }
            finally
            {
                lifetime.Dispose();
                logger.Information("Domain destination discovery ended. Status={Status}, CandidateCount={CandidateCount}, FailureCode={FailureCode}, NativeErrorCode={NativeErrorCode}, LdapErrorCode={LdapErrorCode}, DirectoryResultCode={DirectoryResultCode}.",
                    result?.Status.ToString() ?? "Failed", result?.Candidates.Count ?? 0, result?.ErrorCode,
                    result?.NativeErrorCode, result?.LdapErrorCode, result?.DirectoryResultCode);
            }
        }, CancellationToken.None);
        try { return await work.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return new(null, null, [], DomainOuDiscoveryStatus.Unavailable, "Timeout"); }
        catch (OperationCanceledException) { return new(null, null, [], DomainOuDiscoveryStatus.Canceled, "Canceled"); }
    }

    private static async Task<DomainOuDiscoveryResult> DiscoverOwnedAsync(CancellationToken token)
    {
        string? domain = null;
        string? namingContext = null;
        List<DomainJoinOrganizationalUnitSettings> candidates = [];
        try
        {
            token.ThrowIfCancellationRequested();
            (domain, string controller) = LocateComputerDomain();
            token.ThrowIfCancellationRequested();
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(controller, 389, true, false), null, AuthType.Negotiate);
            connection.Timeout = RequestTimeout;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.AutoReconnect = false;
            connection.SessionOptions.SendTimeout = RequestTimeout;
            connection.Bind();
            token.ThrowIfCancellationRequested();
            var rootRequest = new SearchRequest("", "(objectClass=*)", SearchScope.Base, "defaultNamingContext", "dnsHostName", "supportedControl")
            { TimeLimit = RequestTimeout };
            var root = (SearchResponse)await SendOwnedAsync(connection, rootRequest, token).ConfigureAwait(false);
            if (root.Entries.Count != 1 || root.References.Count > 0) throw new InvalidDataException();
            namingContext = Attribute(root.Entries[0], "defaultNamingContext");
            if (!DistinguishedNameRules.IsWithinDomain(namingContext, domain)) throw new InvalidDataException();
            var request = new SearchRequest(namingContext, "(objectCategory=organizationalUnit)", SearchScope.Subtree, "distinguishedName", "name", "objectGUID")
            { TimeLimit = RequestTimeout };
            var paging = new PageResultRequestControl(256) { IsCritical = true };
            request.Controls.Add(paging);
            request.Controls.Add(new DomainScopeControl { IsCritical = true });
            while (true)
            {
                var response = (SearchResponse)await SendOwnedAsync(connection, request, token).ConfigureAwait(false);
                bool valid = AddEntries(response, candidates, domain);
                PageResultResponseControl[] pages = response.Controls.OfType<PageResultResponseControl>().ToArray();
                if (!valid || response.References.Count > 0 || pages.Length != 1)
                    return new(domain, namingContext, candidates, DomainOuDiscoveryStatus.Incomplete, "Incomplete");
                if (pages[0].Cookie.Length == 0)
                    return new(domain, namingContext, candidates, DomainOuDiscoveryStatus.Complete);
                if (candidates.Count >= 4096)
                    return new(domain, namingContext, candidates, DomainOuDiscoveryStatus.Incomplete, "Limit");
                paging.Cookie = pages[0].Cookie;
            }
        }
        catch (OperationCanceledException) { return new(domain, namingContext, [], DomainOuDiscoveryStatus.Canceled, "Canceled"); }
        catch (DirectoryOperationException ex)
        {
            if (ex.Response is SearchResponse partial && domain is not null) AddEntries(partial, candidates, domain);
            bool incomplete = candidates.Count > 0 || ex.Response?.ResultCode is ResultCode.TimeLimitExceeded or ResultCode.SizeLimitExceeded or ResultCode.Referral;
            return new(domain, namingContext, candidates, incomplete ? DomainOuDiscoveryStatus.Incomplete : DomainOuDiscoveryStatus.Unavailable,
                "DirectoryRead", DirectoryResultCode: ex.Response is null ? null : (int)ex.Response.ResultCode);
        }
        catch (Win32Exception ex) { return new(domain, namingContext, [], DomainOuDiscoveryStatus.Unavailable, "ComputerDomainUnavailable", NativeErrorCode: ex.NativeErrorCode); }
        catch (Exception ex) when (ex is LdapException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return new(domain, namingContext, candidates, candidates.Count > 0 ? DomainOuDiscoveryStatus.Incomplete : DomainOuDiscoveryStatus.Unavailable,
                ex is InvalidOperationException ? "ComputerDomainUnavailable" : "DirectoryRead", LdapErrorCode: (ex as LdapException)?.ErrorCode);
        }
    }

    private static bool AddEntries(SearchResponse response, List<DomainJoinOrganizationalUnitSettings> candidates, string domain)
    {
        bool valid = true;
        foreach (SearchResultEntry entry in response.Entries)
        {
            if (candidates.Count >= 4096) return false;
            string dn = Attribute(entry, "distinguishedName");
            string name = Attribute(entry, "name");
            DirectoryAttribute? guidAttribute = entry.Attributes["objectGUID"];
            if (!DistinguishedNameRules.IsWithinDomain(dn, domain) || name.Length is 0 or > 120 || name.Contains('\0') ||
                guidAttribute?.Count != 1 || guidAttribute[0] is not byte[] { Length: 16 } guid)
            { valid = false; continue; }
            candidates.Add(new() { Id = new Guid(guid).ToString("D"), DisplayName = name, DistinguishedName = dn });
        }
        return valid;
    }

    private static string Attribute(SearchResultEntry entry, string name) => entry.Attributes[name] is { Count: 1 } attribute && attribute[0] is string value
        ? value : string.Empty;

    private static async Task<DirectoryResponse> SendOwnedAsync(LdapConnection connection, DirectoryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<DirectoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object();
        bool settled = false;
        // Begin can synchronously connect/send. Cancellation is observed after it returns; the worker retains ownership.
        IAsyncResult pending = connection.BeginSendRequest(request, RequestTimeout, PartialResultProcessing.NoPartialResultSupport, result =>
        {
            lock (sync)
            {
                if (settled) return;
                settled = true;
                try { completion.TrySetResult(connection.EndSendRequest(result)); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }
        }, null);
        using CancellationTokenRegistration registration = token.Register(() =>
        {
            lock (sync)
            {
                if (settled) return;
                settled = true;
                try { connection.Abort(pending); }
                catch (ArgumentException) { }
                finally { completion.TrySetCanceled(token); }
            }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private static (string Domain, string Controller) LocateComputerDomain()
    {
        nint membership = 0;
        nint locator = 0;
        try
        {
            uint status = NetGetJoinInformation(null, out membership, out int joinStatus);
            if (status != 0) throw new Win32Exception(unchecked((int)status));
            if (joinStatus != 3) throw new InvalidOperationException();
            string flatDomain = Marshal.PtrToStringUni(membership) ?? throw new InvalidDataException();
            status = DsGetDcName(null, flatDomain, 0, null, 0x10 | 0x10000 | 0x40000000, out locator);
            if (status != 0) throw new Win32Exception(unchecked((int)status));
            DomainControllerInfo info = Marshal.PtrToStructure<DomainControllerInfo>(locator);
            string domain = Marshal.PtrToStringUni(info.DomainName) ?? string.Empty;
            string controller = (Marshal.PtrToStringUni(info.DomainControllerName) ?? string.Empty).TrimStart('\\');
            if (!DomainJoinCredentialContext.IsValidDomainName(domain) || !DomainJoinCredentialContext.IsValidDomainName(controller)) throw new InvalidDataException();
            return (DomainJoinCredentialContext.CanonicalizeDomainName(domain), controller);
        }
        finally
        {
            if (locator != 0) NetApiBufferFree(locator);
            if (membership != 0) NetApiBufferFree(membership);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DomainControllerInfo
    {
        public nint DomainControllerName;
        public nint DomainControllerAddress;
        public uint DomainControllerAddressType;
        public Guid DomainGuid;
        public nint DomainName;
        public nint DnsForestName;
        public uint Flags;
        public nint DcSiteName;
        public nint ClientSiteName;
    }

    [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint NetGetJoinInformation(string? server, out nint nameBuffer, out int status);
    [DllImport("netapi32.dll", EntryPoint = "DsGetDcNameW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint DsGetDcName(string? computer, string? domain, nint guid, string? site, uint flags, out nint info);
    [DllImport("netapi32.dll", ExactSpelling = true)]
    private static extern uint NetApiBufferFree(nint buffer);
}

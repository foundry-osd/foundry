// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.DomainJoin;

/// <summary>Locates one writable domain controller of a DNS domain through <c>DsGetDcName</c>.</summary>
public static class DomainControllerLocator
{
    private const uint ForceRediscovery = 0x1;

    // DS_IS_DNS_NAME | DS_WRITABLE_REQUIRED | DS_RETURN_DNS_NAME | DS_DIRECTORY_SERVICE_6_REQUIRED
    private const uint LocatorFlags = 0x10 | 0x20000 | 0x40000000 | 0x1000;

    /// <summary>
    /// Returns the DNS host name of a writable controller of <paramref name="canonicalDomain"/>, without leading backslashes.
    /// Returns <see langword="false"/> with a non-zero <paramref name="nativeError"/> when the locator fails, and with zero when
    /// it answers for another domain or with a controller name that is not a valid DNS name.
    /// </summary>
    /// <param name="canonicalDomain">Domain canonicalized with <see cref="DomainJoinCredentialContext.CanonicalizeDomainName"/>.</param>
    /// <param name="forceRediscovery">Bypasses the locator cache, for a repeated attempt after a failure.</param>
    public static bool TryLocate(string canonicalDomain, bool forceRediscovery, out string controller, out int nativeError)
    {
        controller = string.Empty;
        nativeError = 0;
        nint buffer = 0;
        try
        {
            uint status = DsGetDcName(null, canonicalDomain, 0, null, LocatorFlags | (forceRediscovery ? ForceRediscovery : 0), out buffer);
            if (status != 0)
            {
                nativeError = unchecked((int)status);
                return false;
            }

            var info = Marshal.PtrToStructure<DomainControllerInfo>(buffer);
            string foundDomain = Marshal.PtrToStringUni(info.DomainName) ?? string.Empty;
            string name = (Marshal.PtrToStringUni(info.DomainControllerName) ?? string.Empty).TrimStart('\\');
            if (DomainJoinCredentialContext.CanonicalizeDomainName(foundDomain) != canonicalDomain || !DomainJoinCredentialContext.IsValidDomainName(name)) return false;
            controller = name;
            return true;
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

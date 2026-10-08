// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Foundry.Core.Models.Configuration;
using Microsoft.Win32;
namespace Foundry.PostInstall.Windows;

/// <summary>Provides the local native operations; a join is never automatically retried.</summary>
internal interface INativeDomainJoin
{
    string GetActiveComputerName();
    int SetComputerName(string name);
    int Join(string domainAndDc, string? creationOu, DomainJoinCredentialContext context, ReadOnlySpan<char> password, bool usePendingName);
    DomainMembershipSnapshot GetMembership();

    /// <summary>
    /// Tells Windows OOBE that no user account has to be created, so setup ends on the sign-in screen. Windows
    /// sets the same value itself when an answer file creates an account.
    /// </summary>
    void SkipAccountCreation();
}
/// <summary>Local passwordless membership observation using DNS domain identity and the active startup name.</summary>
internal sealed record DomainMembershipSnapshot(int JoinStatus, string? DomainName, string ActiveComputerName, int? NativeErrorCode = null);

/// <summary>Uses documented installed-Windows APIs without rejoin, unsecured, or hardening-bypass flags.</summary>
internal sealed class NativeDomainJoin : INativeDomainJoin
{
    public string GetActiveComputerName()
    {
        uint count = 0;
        bool probed = GetComputerNameEx(5, 0, ref count);
        int error = Marshal.GetLastPInvokeError();
        if (probed || error != 234 || count is 0 or > 32768) throw new Win32Exception(error);
        nint buffer = Marshal.AllocHGlobal(checked((int)count * 2));
        try
        {
            if (!GetComputerNameEx(5, buffer, ref count)) { error = Marshal.GetLastPInvokeError(); throw new Win32Exception(error); }
            return Marshal.PtrToStringUni(buffer, checked((int)count));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public int SetComputerName(string name) => SetComputerNameEx(5, name) ? 0 : Marshal.GetLastPInvokeError();
    public int Join(string domainAndDc, string? creationOu, DomainJoinCredentialContext context, ReadOnlySpan<char> password, bool usePendingName)
    {
        if (password.IsEmpty || password.Contains('\0')) throw new InvalidDataException("The password is invalid.");
        int characters = checked(password.Length + 1);
        nint native = Marshal.AllocHGlobal(checked(characters * 2));
        try
        {
            for (int index = 0; index < password.Length; index++) Marshal.WriteInt16(native, index * 2, unchecked((short)password[index]));
            Marshal.WriteInt16(native, password.Length * 2, 0);
            return unchecked((int)NetJoinDomain(null, domainAndDc, creationOu, context.AccountName.Trim(), native, 0x1u | 0x2u | (usePendingName ? 0x400u : 0u)));
        }
        finally
        {
            for (int index = 0; index < characters; index++) Marshal.WriteInt16(native, index * 2, 0);
            Marshal.FreeHGlobal(native);
        }
    }
    public DomainMembershipSnapshot GetMembership()
    {
        nint flat = 0; nint dns = 0; string name = string.Empty;
        try
        {
            name = GetActiveComputerName();
            uint status = NetGetJoinInformation(null, out flat, out int joinStatus);
            if (status != 0) return new(joinStatus, null, name, unchecked((int)status));
            if (joinStatus != 3) return new(joinStatus, null, name);
            status = DsRoleGetPrimaryDomainInformation(null, 1, out dns);
            if (status != 0) return new(joinStatus, null, name, unchecked((int)status));
            var info = Marshal.PtrToStructure<PrimaryDomainInfoBasic>(dns);
            string? domain = info.MachineRole is 1 or 3 ? Marshal.PtrToStringUni(info.DomainNameDns) : null;
            return new(joinStatus, domain, name);
        }
        catch (Win32Exception error) { return new(0, null, name, error.NativeErrorCode); }
        finally { if (flat != 0) NetApiBufferFree(flat); if (dns != 0) DsRoleFreeMemory(dns); }
    }
    public void SkipAccountCreation()
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\OOBE", writable: true);
        key.SetValue("UnattendCreatedUser", 1, RegistryValueKind.DWord);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrimaryDomainInfoBasic
    {
        public int MachineRole; public uint Flags; public nint DomainNameFlat; public nint DomainNameDns;
        public nint DomainForestName; public Guid DomainGuid;
    }
    [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint NetJoinDomain(string? server, string domainAndController, string? machineAccountOu, string account, nint password, uint joinOptions);
    [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint NetGetJoinInformation(string? server, out nint buffer, out int status);
    [DllImport("netapi32.dll", ExactSpelling = true)]
    private static extern uint NetApiBufferFree(nint buffer);
    [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint DsRoleGetPrimaryDomainInformation(string? server, int infoLevel, out nint buffer);
    [DllImport("netapi32.dll", ExactSpelling = true)]
    private static extern void DsRoleFreeMemory(nint buffer);
    [DllImport("kernel32.dll", EntryPoint = "SetComputerNameExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetComputerNameEx(int format, string name);
    [DllImport("kernel32.dll", EntryPoint = "GetComputerNameExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComputerNameEx(int format, nint buffer, ref uint count);
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Utilities.Networking;

/// <summary>Classifies the outcome of a deviceless SMB connection.</summary>
public enum SmbConnectStatus
{
    Connected,
    AccessDenied,
    NotFound,
    CredentialConflict,
    Failed
}

/// <summary>Carries the connection status and the Win32 error that produced it.</summary>
public sealed record SmbConnectResult(SmbConnectStatus Status, int Win32Error);

/// <summary>Opens and closes SMB sessions without mapping a drive letter.</summary>
public interface ISmbConnector
{
    /// <summary>
    /// Connects to <paramref name="uncPath"/>. A <see langword="null"/> user name uses the process identity and ignores
    /// <paramref name="password"/>. The password is never copied into a managed string.
    /// </summary>
    SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password);

    /// <summary>Closes the session opened for <paramref name="uncPath"/>; failures are ignored.</summary>
    void Disconnect(string uncPath);
}

/// <summary>Uses <c>WNetAddConnection2W</c> with a null local name, so no drive letter is consumed.</summary>
public sealed class SmbConnector : ISmbConnector
{
    private const uint ResourceTypeDisk = 0x00000001;
    private const uint ConnectTemporary = 0x00000004;
    private const int ErrorAccessDenied = 5;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorLogonFailure = 1326;

    /// <inheritdoc />
    public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uncPath);
        var resource = new NetResource { Type = ResourceTypeDisk, RemoteName = uncPath };
        int error;
        if (userName is null)
        {
            error = WNetAddConnection2(ref resource, 0, null, ConnectTemporary);
            return Map(error);
        }

        char[] buffer = new char[password.Length + 1];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            password.CopyTo(buffer);
            error = WNetAddConnection2(ref resource, handle.AddrOfPinnedObject(), userName, ConnectTemporary);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));
            handle.Free();
        }

        return Map(error);
    }

    /// <inheritdoc />
    public void Disconnect(string uncPath)
    {
        if (!string.IsNullOrWhiteSpace(uncPath))
        {
            _ = WNetCancelConnection2(uncPath, 0, true);
        }
    }

    internal static SmbConnectResult Map(int error) => new(error switch
    {
        0 => SmbConnectStatus.Connected,
        ErrorAccessDenied or ErrorLogonFailure => SmbConnectStatus.AccessDenied,
        ErrorBadNetPath or ErrorBadNetName => SmbConnectStatus.NotFound,
        ErrorSessionCredentialConflict => SmbConnectStatus.CredentialConflict,
        _ => SmbConnectStatus.Failed
    }, error);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetAddConnection2(ref NetResource resource, nint password, string? userName, uint flags);

    [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetCancelConnection2(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}

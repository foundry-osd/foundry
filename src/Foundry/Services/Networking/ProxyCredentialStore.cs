// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Foundry.Services.Networking;

public sealed class ProxyCredential
{
    public ProxyCredential(string username, string domain, string password)
    {
        Username = username;
        Domain = domain;
        Password = password;
    }

    public string Username { get; }

    public string Domain { get; }

    public string Password { get; }
}

public interface IProxyCredentialStore
{
    ProxyCredential? Read();

    void Save(ProxyCredential credential);

    void Delete();
}

internal sealed class ProxyCredentialStore : IProxyCredentialStore
{
    private const string TargetName = "FoundryOSD/Proxy";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public ProxyCredential? Read()
    {
        if (!CredRead(TargetName, CredentialTypeGeneric, 0, out nint credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            string username = credential.UserName ?? string.Empty;
            string password = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / sizeof(char)) ?? string.Empty;
            (string domain, string user) = SplitUsername(username);
            return new ProxyCredential(user, domain, password);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Save(ProxyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        string username = string.IsNullOrWhiteSpace(credential.Domain)
            ? credential.Username
            : $"{credential.Domain}\\{credential.Username}";
        nint passwordPointer = Marshal.StringToCoTaskMemUni(credential.Password);
        try
        {
            var nativeCredential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = credential.Password.Length * sizeof(char),
                CredentialBlob = passwordPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = username
            };

            if (!CredWrite(ref nativeCredential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(passwordPointer);
        }
    }

    public void Delete()
    {
        if (CredDelete(TargetName, CredentialTypeGeneric, 0))
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error);
        }
    }

    private static (string Domain, string Username) SplitUsername(string value)
    {
        int separator = value.IndexOf('\\');
        return separator > 0
            ? (value[..separator], value[(separator + 1)..])
            : (string.Empty, value);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}

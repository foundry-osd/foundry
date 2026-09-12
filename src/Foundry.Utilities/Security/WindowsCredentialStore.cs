// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Utilities.Security;

/// <summary>
/// Stores bounded opaque values in Windows Credential Manager without a plaintext fallback.
/// </summary>
public sealed class WindowsCredentialStore : IWindowsCredentialStore
{
    /// <summary>
    /// Maximum encoded blob length accepted by WinCred (CRED_MAX_CREDENTIAL_BLOB_SIZE).
    /// </summary>
    public const int MaximumSecretSize = 2560;

    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    /// <inheritdoc />
    public WindowsCredential? Read(string target)
    {
        ValidateTarget(target);
        if (!CredRead(target, CredentialTypeGeneric, 0, out nint credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        NativeCredential nativeCredential = default;
        try
        {
            nativeCredential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (nativeCredential.CredentialBlobSize > MaximumSecretSize ||
                (nativeCredential.CredentialBlobSize > 0 && nativeCredential.CredentialBlob == 0))
            {
                throw new InvalidDataException("Windows returned an invalid credential blob.");
            }

            byte[] secret = new byte[(int)nativeCredential.CredentialBlobSize];
            try
            {
                if (secret.Length > 0)
                {
                    Marshal.Copy(nativeCredential.CredentialBlob, secret, 0, secret.Length);
                }

                return new WindowsCredential(secret, nativeCredential.UserName);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(secret);
                throw;
            }
        }
        finally
        {
            try
            {
                if (nativeCredential.CredentialBlob != 0 && nativeCredential.CredentialBlobSize <= MaximumSecretSize)
                {
                    for (int index = 0; index < nativeCredential.CredentialBlobSize; index++)
                    {
                        Marshal.WriteByte(nativeCredential.CredentialBlob, index, 0);
                    }
                }
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }
    }

    /// <inheritdoc />
    public void Write(string target, ReadOnlySpan<byte> secret, string? userName = null)
    {
        ValidateTarget(target);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secret.Length, MaximumSecretSize, nameof(secret));
        if (userName?.Contains('\0') == true)
        {
            throw new ArgumentException("Credential usernames cannot contain null characters.", nameof(userName));
        }

        byte[] secretCopy = secret.ToArray();
        GCHandle pinnedSecret = default;
        try
        {
            pinnedSecret = GCHandle.Alloc(secretCopy, GCHandleType.Pinned);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretCopy.Length,
                CredentialBlob = secretCopy.Length == 0 ? 0 : pinnedSecret.AddrOfPinnedObject(),
                Persist = CredentialPersistLocalMachine,
                UserName = userName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretCopy);
            if (pinnedSecret.IsAllocated)
            {
                pinnedSecret.Free();
            }
        }
    }

    /// <inheritdoc />
    public void Delete(string target)
    {
        ValidateTarget(target);
        if (CredDelete(target, CredentialTypeGeneric, 0))
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error);
        }
    }

    private static void ValidateTarget(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        if (target.Contains('\0'))
        {
            throw new ArgumentException("Credential targets cannot contain null characters.", nameof(target));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
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

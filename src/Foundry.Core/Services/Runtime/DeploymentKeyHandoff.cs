// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Foundry.Core.Services.Runtime;

/// <summary>Contract of the one-shot deployment key hand-off Connect → Bootstrap → Deploy.</summary>
/// <remarks>
/// <para>
/// Foundry Bootstrap owns two named pipes per boot: Foundry Connect writes the 32-byte deployment key once into the first
/// one after a successful unlock, and Foundry Deploy reads it once from the second one. Each pipe has a random 128-bit name
/// handed to exactly one child through one environment variable, accepts one connection from the launched child process
/// only (the server checks the client process id), is created non-inheritable and grants access to the current user only.
/// The DACL names the user SID and .NET does not set <c>PIPE_REJECT_REMOTE_CLIENTS</c>, so the client process id check is
/// the backstop against a remote client authenticated as the same account.
/// </para>
/// <para>
/// Anonymous pipes and handle inheritance are not used: <c>Process.Start</c> makes every inheritable handle visible to every
/// child on .NET 10 and the next major release restricts inheritance to the standard streams, while a non-inheritable named
/// pipe works with both and needs no <c>CreateProcess</c> interop.
/// </para>
/// <para>
/// The key is never logged, persisted or put in an exception message. A key that does not arrive is not an error: Deploy
/// then prompts for the deployment password as before, and a received key is still validated by the media key check.
/// </para>
/// </remarks>
public static class DeploymentKeyHandoff
{
    /// <summary>Environment variable naming the pipe Foundry Connect writes the key to.</summary>
    public const string SendPipeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_KEY_SEND_PIPE";

    /// <summary>Environment variable naming the pipe Foundry Deploy reads the key from.</summary>
    public const string ReceivePipeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_KEY_RECEIVE_PIPE";

    /// <summary>Length of the deployment key, in bytes.</summary>
    public const int KeyLength = 32;

    /// <summary>Longest wait for a pipe connection, the expected peer or one message, on both ends.</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private const string PipeNamePrefix = "Foundry.DeploymentKey.";
    private const int PipeNameRandomBytes = 16;

    /// <summary>Creates a new random pipe name, <c>Foundry.DeploymentKey.</c> followed by 32 lowercase hexadecimal digits.</summary>
    public static string CreatePipeName() =>
        PipeNamePrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PipeNameRandomBytes));

    /// <summary>
    /// Returns whether <paramref name="pipeName"/> has exactly the form produced by <see cref="CreatePipeName"/>; a client
    /// never opens any other pipe, so an environment variable cannot redirect the key elsewhere.
    /// </summary>
    public static bool IsValidPipeName([NotNullWhen(true)] string? pipeName)
    {
        if (pipeName is null
            || pipeName.Length != PipeNamePrefix.Length + (PipeNameRandomBytes * 2)
            || !pipeName.StartsWith(PipeNamePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in pipeName.AsSpan(PipeNamePrefix.Length))
        {
            if (!char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates the security of a hand-off pipe server: a protected DACL with one full-control allow rule for the current
    /// user, and the current token's default owner as owner, which is what a <see cref="PipeOptions.CurrentUserOnly"/>
    /// client compares, elevated or not.
    /// </summary>
    public static PipeSecurity CreateServerSecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current token has no user.");
        SecurityIdentifier owner = identity.Owner ?? throw new InvalidOperationException("The current token has no owner.");

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>Writes the deployment key once to the named hand-off pipe.</summary>
    /// <param name="pipeName">Pipe name read from <see cref="SendPipeEnvironmentVariable"/>.</param>
    /// <param name="key">Deployment key; the caller keeps ownership of the buffer and zeroes it.</param>
    /// <param name="cancellationToken">Caller cancellation, which is the only failure that propagates.</param>
    /// <returns><see langword="true"/> when the key was written; <see langword="false"/> on an invalid name or any pipe failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not <see cref="KeyLength"/> bytes long.</exception>
    public static async Task<bool> TrySendAsync(string? pipeName, ReadOnlyMemory<byte> key, CancellationToken cancellationToken)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException("The deployment key has an invalid length.", nameof(key));
        }

        if (!IsValidPipeName(pipeName))
        {
            return false;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ClientTimeout);
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            await client.ConnectAsync(ClientTimeout, deadline.Token).ConfigureAwait(false);
            await client.WriteAsync(key, deadline.Token).ConfigureAwait(false);
            await client.FlushAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsPipeFailure(exception, cancellationToken))
        {
            return false;
        }
    }

    /// <summary>Reads the deployment key once from the named hand-off pipe.</summary>
    /// <param name="pipeName">Pipe name read from <see cref="ReceivePipeEnvironmentVariable"/>.</param>
    /// <param name="cancellationToken">Caller cancellation, which is the only failure that propagates.</param>
    /// <returns>
    /// A pinned <see cref="KeyLength"/>-byte array the caller must zero with <see cref="CryptographicOperations.ZeroMemory"/>,
    /// or <see langword="null"/> on an invalid name, a short message or any pipe failure.
    /// </returns>
    public static async Task<byte[]?> TryReceiveAsync(string? pipeName, CancellationToken cancellationToken)
    {
        if (!IsValidPipeName(pipeName))
        {
            return null;
        }

        byte[] key = GC.AllocateArray<byte>(KeyLength, pinned: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ClientTimeout);
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In,
                PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            await client.ConnectAsync(ClientTimeout, deadline.Token).ConfigureAwait(false);
            await client.ReadExactlyAsync(key, deadline.Token).ConfigureAwait(false);
            return key;
        }
        catch (Exception exception) when (IsPipeFailure(exception, cancellationToken))
        {
            CryptographicOperations.ZeroMemory(key);
            return null;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    // EndOfStreamException (short message) is an IOException.
    private static bool IsPipeFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is IOException or TimeoutException or UnauthorizedAccessException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}

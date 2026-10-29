// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Outcome of one operation run against a network cache share.</summary>
public enum NetworkCacheShareAccessStatus
{
    /// <summary>The share was opened and the operation completed.</summary>
    Completed,
    /// <summary>The share could not be opened; the operation did not run.</summary>
    Unreachable,
    /// <summary>The share was opened but the operation failed.</summary>
    Failed
}

/// <summary>Carries the outcome of one share operation, its value when it completed, and what made it fail.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Value">The operation value when <paramref name="Status"/> is <see cref="NetworkCacheShareAccessStatus.Completed"/>.</param>
/// <param name="Connection">The SMB connection result, or <see langword="null"/> when no connection was attempted.</param>
/// <param name="Error">The exception that made the operation fail, never an exception that carries a password.</param>
public sealed record NetworkCacheShareAccessResult<T>(NetworkCacheShareAccessStatus Status, T? Value, SmbConnectResult? Connection, Exception? Error);

/// <summary>
/// Opens a network cache share the way Foundry OSD does, runs an operation against <c>&lt;share&gt;\Foundry</c> on the thread
/// pool, and closes the session it opened. A share with an account gets its own SMB session, closed without forcing open
/// files closed; a share without one is reached as the signed-in Windows user, through the session Windows already manages,
/// so a connection the user opened, for example in File Explorer, is never closed. Share errors become results; only
/// cancellation propagates.
/// </summary>
public sealed class NetworkCacheShareAccess
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    private readonly ISmbConnector connector;
    private readonly TimeSpan connectTimeout;

    /// <summary>Creates the access with the connection timeout that Foundry Deploy also uses.</summary>
    public NetworkCacheShareAccess(ISmbConnector connector)
        : this(connector, DefaultConnectTimeout)
    {
    }

    internal NetworkCacheShareAccess(ISmbConnector connector, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(connector);
        this.connector = connector;
        this.connectTimeout = connectTimeout;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> against the content root of <paramref name="share"/>. The password copy is taken on the
    /// calling thread, so a secret store that is not thread-safe is only read there, and is cleared right after the connection
    /// attempt, or when the run is cancelled before it. A connection that does not answer within the timeout makes the share
    /// unreachable; if it completes later, its session is closed.
    /// </summary>
    /// <param name="settings">The section that owns the share and its account.</param>
    /// <param name="share">The share to open.</param>
    /// <param name="getPasswordCopy">Returns an owned copy of an account password by account id, or <see langword="null"/>.</param>
    /// <param name="operation">Receives <c>&lt;share&gt;\Foundry</c>; it may throw for any share error.</param>
    /// <param name="cancellationToken">Cancels the operation; cancellation is rethrown after the share is closed.</param>
    public async Task<NetworkCacheShareAccessResult<T>> RunAsync<T>(
        NetworkCacheSettings settings,
        NetworkCacheShare share,
        Func<string, char[]?> getPasswordCopy,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(getPasswordCopy);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (share.AccountId is not { } accountId)
        {
            // Directory.Exists blocks for the SMB timeout on a dead server, so it runs on the pool like the operation.
            return await Task.Run(() => Directory.Exists(share.Path)
                ? RunOperationAsync(share.Path, null, operation, cancellationToken)
                : Task.FromResult(new NetworkCacheShareAccessResult<T>(NetworkCacheShareAccessStatus.Unreachable, default, null, null)),
                CancellationToken.None).ConfigureAwait(false);
        }

        NetworkCacheAccount? account = settings.Accounts.FirstOrDefault(candidate => string.Equals(candidate.Id, accountId, StringComparison.Ordinal));
        if (account is null)
            return new(NetworkCacheShareAccessStatus.Unreachable, default, null, new InvalidDataException("The share references an account that does not exist."));
        char[]? password = getPasswordCopy(accountId);

        // The pool work is not given the token: it must start to clear the password copy.
        Task<SmbConnectResult> connecting = Task.Run(() => Connect(share.Path, account.UserName, password, cancellationToken), CancellationToken.None);
        SmbConnectResult connection;
        try
        {
            connection = await connecting.WaitAsync(connectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // A connection that completes after the wait must not stay open with the account credentials.
            _ = connecting.ContinueWith(
                task => { if (task.Result.Status == SmbConnectStatus.Connected) connector.Disconnect(share.Path, force: false); },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            if (exception is OperationCanceledException) throw;
            return new(NetworkCacheShareAccessStatus.Unreachable, default, null, exception);
        }

        if (connection.Status != SmbConnectStatus.Connected) return new(NetworkCacheShareAccessStatus.Unreachable, default, connection, null);
        try
        {
            return await Task.Run(() => RunOperationAsync(share.Path, connection, operation, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            connector.Disconnect(share.Path, force: false);
        }
    }

    private SmbConnectResult Connect(string sharePath, string userName, char[]? password, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return connector.Connect(sharePath, userName, password);
        }
        finally
        {
            if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    private static async Task<NetworkCacheShareAccessResult<T>> RunOperationAsync<T>(
        string sharePath,
        SmbConnectResult? connection,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            T value = await operation(NetworkCacheLayout.ContentRoot(sharePath), cancellationToken).ConfigureAwait(false);
            return new(NetworkCacheShareAccessStatus.Completed, value, connection, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(NetworkCacheShareAccessStatus.Failed, default, connection, exception);
        }
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Services.Runtime;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace Foundry.Bootstrap.Processes;

/// <summary>
/// Bootstrap-owned relay of the deployment key: one inbound pipe for Foundry Connect, one outbound pipe for Foundry Deploy.
/// </summary>
/// <remarks>
/// <para>
/// Each pipe has a fresh random name (<see cref="DeploymentKeyHandoff.CreatePipeName"/>), one instance, a non-inheritable
/// handle and the DACL of <see cref="DeploymentKeyHandoff.CreateServerSecurity"/>. It accepts exactly one connection and is
/// disposed after it; the client process id (<c>GetNamedPipeClientProcessId</c>, read at connection time) must equal the
/// launched child's id, so another process can neither plant nor read a key. Anonymous pipes are not used because
/// <c>Process.Start</c> exposes every inheritable handle to every child on .NET 10.
/// </para>
/// <para>
/// The key lives only in one pinned 32-byte buffer that is zeroed when serving ends and on disposal; read buffers are zeroed
/// in <see langword="finally"/> of their exchange, so for an exchange still in flight at disposal that zeroing is eventual
/// (it runs once the disposed pipe fails the pending read). Nothing is written to disk, and logs carry outcome categories, process ids, lengths and
/// exception type names only, never key bytes, pipe names or exception messages. No hand-off failure escapes a loop.
/// </para>
/// </remarks>
internal sealed class DeploymentKeyRelay(ILogger logger) : IDeploymentKeyRelay
{
    private readonly byte[] key = GC.AllocateArray<byte>(DeploymentKeyHandoff.KeyLength, pinned: true);
    private readonly CancellationTokenSource disposal = new();
    private readonly Lock gate = new();
    private PipeSession? inbound;
    private PipeSession? outbound;
    private bool keyHeld;
    private bool disposed;

    /// <summary>The pinned key buffer; all zeros after <see cref="EndServeAsync"/> or <see cref="Dispose"/>.</summary>
    internal byte[]? HeldKeyForTesting => key;

    /// <summary>Creates one non-inheritable, single-instance hand-off pipe server with the hand-off security.</summary>
    /// <remarks>
    /// <see cref="PipeOptions.FirstPipeInstance"/> is belt-and-braces: .NET already requests the first instance when the
    /// maximum instance count is 1, so creation fails when another process created the name first.
    /// </remarks>
    internal static NamedPipeServerStream CreateServer(string pipeName, PipeDirection direction) =>
        NamedPipeServerStreamAcl.Create(pipeName, direction, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, DeploymentKeyHandoff.CreateServerSecurity(),
            HandleInheritability.None);

    /// <inheritdoc />
    public string BeginReceive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (inbound is not null)
        {
            throw new InvalidOperationException("The deployment key relay already received.");
        }

        string pipeName = DeploymentKeyHandoff.CreatePipeName();
        inbound = new PipeSession(CreateServer(pipeName, PipeDirection.In));
        inbound.Completion = ReceiveAsync(inbound);
        return pipeName;
    }

    /// <inheritdoc />
    public void ExpectSender(int processId) => Expect(inbound, processId);

    /// <inheritdoc />
    public async Task<bool> EndReceiveAsync()
    {
        if (inbound is null)
        {
            return false;
        }

        await inbound.EndAsync().ConfigureAwait(false);
        return keyHeld;
    }

    /// <inheritdoc />
    public string? BeginServe()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!keyHeld)
        {
            return null;
        }

        if (outbound is not null)
        {
            throw new InvalidOperationException("The deployment key relay already served.");
        }

        string pipeName = DeploymentKeyHandoff.CreatePipeName();
        outbound = new PipeSession(CreateServer(pipeName, PipeDirection.Out));
        outbound.Completion = ServeAsync(outbound);
        return pipeName;
    }

    /// <inheritdoc />
    public void ExpectReceiver(int processId) => Expect(outbound, processId);

    /// <inheritdoc />
    public async Task<bool> EndServeAsync()
    {
        bool delivered = outbound is not null && await outbound.EndAsync().ConfigureAwait(false);
        ZeroKey();
        return delivered;
    }

    /// <summary>
    /// Cancels both pipes, disposes them with their cancellation sources and zeroes the key at once; the read buffer of an
    /// exchange still in flight is zeroed when that exchange ends.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        disposal.Cancel();
        inbound?.Dispose();
        outbound?.Dispose();
        ZeroKey();
        disposal.Dispose();
    }

    // No pipe (BeginServe without a key, or Begin never called) means nothing to expect, like End* without a pipe.
    private static void Expect(PipeSession? session, int processId) => session?.ExpectedProcess.TrySetResult(processId);

    private async Task<bool> ReceiveAsync(PipeSession session)
    {
        // One byte more than a key, so an oversized message is detected without reading it all.
        byte[] message = GC.AllocateArray<byte>(DeploymentKeyHandoff.KeyLength + 1, pinned: true);
        try
        {
            if (!await AcceptExpectedClientAsync(session).ConfigureAwait(false))
            {
                return false;
            }

            using CancellationTokenSource deadline = CreateDeadline();
            int length = 0;
            while (length < message.Length)
            {
                int read = await session.Server.ReadAsync(message.AsMemory(length), deadline.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length != DeploymentKeyHandoff.KeyLength)
            {
                logger.Warning("Deployment key hand-off received an invalid message. Length={Length}", length);
                return false;
            }

            lock (gate)
            {
                if (disposed)
                {
                    return false;
                }

                message.AsSpan(0, DeploymentKeyHandoff.KeyLength).CopyTo(key);
                keyHeld = true;
            }

            logger.Information("Deployment key received from Foundry Connect.");
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(session, exception);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
            session.Server.Dispose();
        }
    }

    private async Task<bool> ServeAsync(PipeSession session)
    {
        try
        {
            if (!await AcceptExpectedClientAsync(session).ConfigureAwait(false))
            {
                return false;
            }

            using CancellationTokenSource deadline = CreateDeadline();
            await session.Server.WriteAsync(key, deadline.Token).ConfigureAwait(false);
            await session.Server.FlushAsync(deadline.Token).ConfigureAwait(false);
            // With a zero-byte output quota the write completes only once the client has read every byte, so the drain
            // returns at once; it fails the write when the client closed without reading.
            session.Server.WaitForPipeDrain();
            logger.Information("Deployment key handed to Foundry Deploy.");
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(session, exception);
            return false;
        }
        finally
        {
            session.Server.Dispose();
        }
    }

    // The client process id is read as soon as the connection exists, before the client can disconnect.
    private async Task<bool> AcceptExpectedClientAsync(PipeSession session)
    {
        await session.Server.WaitForConnectionAsync(session.Stop.Token).ConfigureAwait(false);
        session.Connected = true;
        bool known = GetNamedPipeClientProcessId(session.Server.SafePipeHandle, out uint clientProcessId);
        int expectedProcessId = await session.ExpectedProcess.Task
            .WaitAsync(DeploymentKeyHandoff.ClientTimeout, disposal.Token).ConfigureAwait(false);
        if (known && clientProcessId == (uint)expectedProcessId)
        {
            return true;
        }

        logger.Warning("Deployment key hand-off refused a client. ExpectedProcessId={ExpectedProcessId}, ClientProcessId={ClientProcessId}",
            expectedProcessId, known ? clientProcessId : null);
        return false;
    }

    // After a connection, only the deadline or disposal stops the exchange, never EndReceiveAsync/EndServeAsync, so a key
    // already in the pipe when the child exits is still read.
    private CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);
        deadline.CancelAfter(DeploymentKeyHandoff.ClientTimeout);
        return deadline;
    }

    private void LogFailure(PipeSession session, Exception exception)
    {
        bool stopped = disposal.IsCancellationRequested || (!session.Connected && session.Stop.IsCancellationRequested);
        if (stopped && exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
            return;
        }

        logger.Warning("Deployment key hand-off failed. FailureType={FailureType}", exception.GetType().Name);
    }

    private void ZeroKey()
    {
        lock (gate)
        {
            CryptographicOperations.ZeroMemory(key);
            keyHeld = false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    /// <summary>One single-connection pipe with its expected peer and its stop signal.</summary>
    private sealed class PipeSession(NamedPipeServerStream server) : IDisposable
    {
        public NamedPipeServerStream Server { get; } = server;

        public TaskCompletionSource<int> ExpectedProcess { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Cancels only the wait for a connection.</summary>
        public CancellationTokenSource Stop { get; } = new();

        public Task<bool> Completion { get; set; } = Task.FromResult(false);

        /// <summary>Set once a client connected; read on the same asynchronous flow.</summary>
        public bool Connected { get; set; }

        public async Task<bool> EndAsync()
        {
            try
            {
                Stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The relay was disposed, which already cancelled the wait for a connection.
            }

            return await Completion.ConfigureAwait(false);
        }

        public void Dispose()
        {
            Stop.Cancel();
            Server.Dispose();
            Stop.Dispose();
        }
    }
}

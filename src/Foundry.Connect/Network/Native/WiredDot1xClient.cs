// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Outcome of one <c>netsh lan</c> command.
/// </summary>
/// <param name="Succeeded">Whether the process completed with exit code 0.</param>
/// <param name="Status">How the process ended; <see cref="ConnectProcessStatus.StartFailed"/> also covers a refused value.</param>
/// <param name="ExitCode">The process exit code, or -1 when no exit code exists.</param>
public sealed record WiredCommandResult(bool Succeeded, ConnectProcessStatus Status, int ExitCode);

/// <summary>
/// The authentication state of one wired interface.
/// </summary>
/// <param name="InterfaceName">The interface name reported by netsh.</param>
/// <param name="Status">The recognized state, or <see langword="null"/> when the state text is not known.</param>
public sealed record WiredInterfaceAuthentication(string InterfaceName, Dot1xAuthenticationStatus? Status);

/// <summary>
/// Applies wired 802.1X profiles and reads the authentication state through <c>netsh lan</c>.
/// </summary>
public interface IWiredDot1xClient
{
    /// <summary>Adds the wired profile file with <c>netsh lan add profile</c>.</summary>
    Task<WiredCommandResult> AddProfileAsync(string profilePath, CancellationToken cancellationToken);

    /// <summary>Restarts authentication on one interface with <c>netsh lan reconnect</c>.</summary>
    Task<WiredCommandResult> ReconnectAsync(string interfaceName, CancellationToken cancellationToken);

    /// <summary>Reads the state of every wired interface. The list is empty when the command fails or times out.</summary>
    Task<IReadOnlyList<WiredInterfaceAuthentication>> ReadAuthenticationAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IWiredDot1xClient"/> backed by the System32 <c>netsh.exe</c>.
/// </summary>
/// <remarks>
/// Arguments stay raw <c>key="value"</c> text (L2-17), so a value containing a double quote is refused before any process starts.
/// </remarks>
public sealed class WiredDot1xClient(IConnectProcessRunner runner) : IWiredDot1xClient
{
    /// <summary>Gets the time allowed for adding a profile.</summary>
    public static readonly TimeSpan AddProfileTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets the time allowed for reconnecting an interface.</summary>
    public static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Gets the time allowed for listing the interfaces.</summary>
    public static readonly TimeSpan ShowInterfacesTimeout = TimeSpan.FromSeconds(10);

    private static readonly WiredCommandResult RefusedValue = new(false, ConnectProcessStatus.StartFailed, -1);

    /// <summary>Gets the absolute path of the system <c>netsh.exe</c>, never resolved through <c>PATH</c>.</summary>
    public static string NetshPath => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    /// <inheritdoc />
    public Task<WiredCommandResult> AddProfileAsync(string profilePath, CancellationToken cancellationToken) =>
        RunCommandAsync($"lan add profile filename=\"{profilePath}\"", profilePath, AddProfileTimeout, cancellationToken);

    /// <inheritdoc />
    public Task<WiredCommandResult> ReconnectAsync(string interfaceName, CancellationToken cancellationToken) =>
        RunCommandAsync($"lan reconnect interface=\"{interfaceName}\"", interfaceName, ReconnectTimeout, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WiredInterfaceAuthentication>> ReadAuthenticationAsync(CancellationToken cancellationToken)
    {
        ConnectProcessResult result = await runner
            .RunAsync(NetshPath, "lan show interfaces", ShowInterfacesTimeout, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? NetshLanInterfaceParser.Parse(result.StandardOutput) : [];
    }

    private async Task<WiredCommandResult> RunCommandAsync(
        string arguments,
        string value,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('"', StringComparison.Ordinal))
        {
            return RefusedValue;
        }

        ConnectProcessResult result = await runner
            .RunAsync(NetshPath, arguments, timeout, cancellationToken)
            .ConfigureAwait(false);

        return new WiredCommandResult(result.IsSuccess, result.Status, result.ExitCode);
    }
}

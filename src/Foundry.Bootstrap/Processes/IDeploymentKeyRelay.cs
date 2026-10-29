// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Processes;

/// <summary>
/// Relays the deployment key once from Foundry Connect to Foundry Deploy through two Bootstrap-owned named pipes
/// (spec 5.7, D11). See <see cref="Foundry.Core.Services.Runtime.DeploymentKeyHandoff"/> for the pipe contract.
/// </summary>
/// <remarks>
/// Call order for one boot: <see cref="BeginReceive"/> before Connect starts, <see cref="ExpectSender"/> once Connect has a
/// process id, <see cref="EndReceiveAsync"/> after Connect exited; then <see cref="BeginServe"/> before Deploy starts,
/// <see cref="ExpectReceiver"/> once Deploy has a process id and <see cref="EndServeAsync"/> once Deploy is ready or failed.
/// Only the <c>Begin</c> members throw, with <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>, when a
/// pipe cannot be created (for example when its name already exists); no other hand-off failure throws, and a missing key
/// only makes Deploy prompt for the password.
/// </remarks>
internal interface IDeploymentKeyRelay : IDisposable
{
    /// <summary>Creates the inbound pipe, starts listening for one connection and returns the pipe name for Connect.</summary>
    string BeginReceive();

    /// <summary>Sets the launched Connect process id; any other client is refused.</summary>
    void ExpectSender(int processId);

    /// <summary>Stops listening (Connect exited) and returns whether a valid key is held.</summary>
    Task<bool> EndReceiveAsync();

    /// <summary>Returns <see langword="null"/> without a held key; otherwise creates the outbound pipe, serves the key once and returns the pipe name for Deploy.</summary>
    string? BeginServe();

    /// <summary>Sets the launched Deploy process id; any other client is refused.</summary>
    void ExpectReceiver(int processId);

    /// <summary>Stops serving, zeroes the key and returns whether Deploy read it.</summary>
    Task<bool> EndServeAsync();
}

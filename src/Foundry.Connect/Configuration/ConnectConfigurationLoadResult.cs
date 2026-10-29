// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Carries the outcome of loading the Foundry.Connect runtime configuration; configuration problems are reported
/// here instead of being thrown.
/// </summary>
/// <param name="Configuration">The normalized configuration, or <see langword="null"/> when loading failed.</param>
/// <param name="FailureMessage">A message without secret material describing why loading failed, or <see langword="null"/> on success.</param>
/// <param name="UpdateReason">Why rebuilding the boot media is recommended; <see cref="BootMediaUpdateReason.None"/> when loading failed.</param>
public sealed record ConnectConfigurationLoadResult(
    FoundryConnectConfigurationDocument? Configuration,
    string? FailureMessage,
    BootMediaUpdateReason UpdateReason)
{
    /// <summary>
    /// Gets the session-only secrets decrypted while loading, or <see langword="null"/> when loading failed.
    /// They travel beside the configuration so they never round-trip through the shared configuration contract.
    /// </summary>
    public ConnectRuntimeSecrets? Secrets { get; init; }

    /// <summary>
    /// Gets whether the configuration was loaded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Configuration), nameof(Secrets))]
    [MemberNotNullWhen(false, nameof(FailureMessage))]
    public bool IsSuccess => Configuration is not null;

    /// <summary>
    /// Creates the result of a successful load.
    /// </summary>
    public static ConnectConfigurationLoadResult Success(
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets,
        BootMediaUpdateReason updateReason) =>
        new(configuration, null, updateReason) { Secrets = secrets };

    /// <summary>
    /// Creates the result of a failed load.
    /// </summary>
    public static ConnectConfigurationLoadResult Failure(string failureMessage) =>
        new(null, failureMessage, BootMediaUpdateReason.None);
}

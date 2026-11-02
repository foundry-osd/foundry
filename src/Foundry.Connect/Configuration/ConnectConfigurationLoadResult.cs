// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    /// Gets the original exception that made loading fail, or <see langword="null"/> on success. Its message and inner
    /// exceptions can carry the configuration file path, so startup diagnostics report
    /// <see cref="CreateDiagnosticFailure"/> instead of this exception.
    /// </summary>
    public Exception? FailureException { get; init; }

    /// <summary>
    /// Gets whether the configuration was loaded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Configuration), nameof(Secrets))]
    [MemberNotNullWhen(false, nameof(FailureMessage), nameof(FailureException))]
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
    /// <param name="failureMessage">A message without secret material describing why loading failed.</param>
    /// <param name="failureException">The original exception that made loading fail.</param>
    public static ConnectConfigurationLoadResult Failure(string failureMessage, Exception failureException) =>
        new(null, failureMessage, BootMediaUpdateReason.None) { FailureException = failureException };

    /// <summary>
    /// Creates the exception that startup diagnostics report for a failed load. It keeps the failure kind (the type
    /// and error code of the underlying cause) but drops the message, inner exception and stack trace of
    /// <see cref="FailureException"/>, because startup diagnostics mirror the record to the remote Logs stream and
    /// error tracking and those parts can carry the configuration file path.
    /// </summary>
    /// <exception cref="InvalidOperationException">Loading succeeded.</exception>
    public Exception CreateDiagnosticFailure()
    {
        Exception failure = FailureException ?? throw new InvalidOperationException("The configuration was loaded successfully.");
        Exception cause = failure.InnerException ?? failure;
        return new FoundryConnectConfigurationException(string.Create(
            CultureInfo.InvariantCulture,
            $"Foundry.Connect configuration could not be loaded. FailureType={cause.GetType().Name}, ErrorCode={cause.HResult}"));
    }
}

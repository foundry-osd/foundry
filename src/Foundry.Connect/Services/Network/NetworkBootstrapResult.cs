// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Represents the user-visible outcome of a handled network bootstrap operation.
/// </summary>
public sealed record NetworkBootstrapResult(string StatusMessage, IReadOnlyList<NetworkBootstrapHandledFailure> HandledFailures)
{
    /// <summary>
    /// Creates a handled result without remote-diagnostic failures.
    /// </summary>
    public static NetworkBootstrapResult Success(string statusMessage)
    {
        return new NetworkBootstrapResult(statusMessage, []);
    }

    /// <summary>
    /// Creates a handled result with one or more remote-diagnostic failures.
    /// </summary>
    public static NetworkBootstrapResult Failed(string statusMessage, params NetworkBootstrapHandledFailure[] handledFailures)
    {
        return new NetworkBootstrapResult(statusMessage, handledFailures);
    }
}

/// <summary>
/// Contains privacy-safe handled failure fields for remote diagnostics.
/// </summary>
public sealed record NetworkBootstrapHandledFailure(string Kind, string Reason, string? Code = null);

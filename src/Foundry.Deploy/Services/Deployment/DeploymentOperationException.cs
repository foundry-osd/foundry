// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Represents a deployment exception with a structured, telemetry-safe failure.
/// </summary>
public sealed class DeploymentOperationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeploymentOperationException"/> class.
    /// </summary>
    /// <param name="failure">Telemetry-safe failure details.</param>
    /// <param name="message">Detailed local diagnostic message.</param>
    public DeploymentOperationException(DeploymentFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeploymentOperationException"/> class.
    /// </summary>
    /// <param name="failure">Telemetry-safe failure details.</param>
    /// <param name="message">Detailed local diagnostic message.</param>
    /// <param name="innerException">Underlying exception.</param>
    public DeploymentOperationException(
        DeploymentFailure failure,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>
    /// Gets the telemetry-safe failure details.
    /// </summary>
    public DeploymentFailure Failure { get; }
}

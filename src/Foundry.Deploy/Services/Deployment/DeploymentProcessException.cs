// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Represents a required deployment process that returned a non-zero exit code.
/// </summary>
public sealed class DeploymentProcessException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeploymentProcessException"/> class.
    /// </summary>
    /// <param name="message">Detailed local diagnostic message.</param>
    /// <param name="exitCode">Process exit code.</param>
    public DeploymentProcessException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeploymentProcessException"/> class.
    /// </summary>
    /// <param name="message">Detailed local diagnostic message.</param>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="innerException">Earlier failure preserved by cleanup handling.</param>
    public DeploymentProcessException(string message, int exitCode, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Foundry.Deploy.Services.System;
using UtilityProcessStartException = Foundry.Utilities.Processes.ProcessStartException;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Converts exceptions into stable, telemetry-safe deployment failure details.
/// </summary>
public static class DeploymentFailureClassifier
{
    /// <summary>
    /// Classifies an exception without copying its message or other high-cardinality data.
    /// </summary>
    /// <param name="exception">Exception to classify.</param>
    /// <param name="operationName">Logical deployment operation active when the exception occurred.</param>
    /// <returns>Telemetry-safe failure details.</returns>
    public static DeploymentFailure Classify(Exception exception, string operationName)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DeploymentOperationException deploymentException)
        {
            return deploymentException.Failure;
        }

        return exception switch
        {
            DeploymentProcessException processException => new(
                operationName,
                DeploymentFailureKinds.Process,
                DeploymentFailureReasons.NonZeroExit,
                processException.ExitCode.ToString(CultureInfo.InvariantCulture)),
            UtilityProcessStartException processStartException => new(
                operationName,
                DeploymentFailureKinds.Process,
                DeploymentFailureReasons.StartFailed,
                processStartException.NativeErrorCode?.ToString(CultureInfo.InvariantCulture)),
            Win32Exception win32Exception => new(
                operationName,
                DeploymentFailureKinds.Process,
                DeploymentFailureReasons.StartFailed,
                win32Exception.NativeErrorCode.ToString(CultureInfo.InvariantCulture)),
            HttpRequestException { StatusCode: HttpStatusCode statusCode } => new(
                operationName,
                DeploymentFailureKinds.Http,
                DeploymentFailureReasons.HttpStatus,
                ((int)statusCode).ToString(CultureInfo.InvariantCulture)),
            HttpRequestException => new(
                operationName,
                DeploymentFailureKinds.Http,
                DeploymentFailureReasons.TransportError),
            FileNotFoundException or DirectoryNotFoundException => new(
                operationName,
                DeploymentFailureKinds.Io,
                DeploymentFailureReasons.NotFound),
            UnauthorizedAccessException => new(
                operationName,
                DeploymentFailureKinds.Io,
                DeploymentFailureReasons.AccessDenied),
            TimeoutException => new(
                operationName,
                DeploymentFailureKinds.Timeout,
                DeploymentFailureReasons.DeadlineExceeded),
            CryptographicException => new(
                operationName,
                DeploymentFailureKinds.Cryptography,
                DeploymentFailureReasons.CryptographicError),
            InvalidDataException => new(
                operationName,
                DeploymentFailureKinds.Validation,
                DeploymentFailureReasons.InvalidPayload),
            ArgumentException => new(
                operationName,
                DeploymentFailureKinds.Validation,
                DeploymentFailureReasons.InvalidInput),
            InvalidOperationException => new(
                operationName,
                DeploymentFailureKinds.Validation,
                DeploymentFailureReasons.InvalidState),
            IOException => new(
                operationName,
                DeploymentFailureKinds.Io,
                DeploymentFailureReasons.UnexpectedException),
            _ => new(
                operationName,
                DeploymentFailureKinds.Unexpected,
                DeploymentFailureReasons.UnexpectedException)
        };
    }
}

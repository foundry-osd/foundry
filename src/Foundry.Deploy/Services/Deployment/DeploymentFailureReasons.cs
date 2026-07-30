// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines stable deployment failure reasons used by telemetry.
/// </summary>
public static class DeploymentFailureReasons
{
    public const string InvalidInput = "invalid_input";
    public const string InvalidState = "invalid_state";
    public const string MissingResource = "missing_resource";
    public const string NotFound = "not_found";
    public const string AccessDenied = "access_denied";
    public const string NonZeroExit = "non_zero_exit";
    public const string StartFailed = "start_failed";
    public const string HttpStatus = "http_status";
    public const string TransportError = "transport_error";
    public const string InvalidPayload = "invalid_payload";
    public const string DeadlineExceeded = "deadline_exceeded";
    public const string CryptographicError = "cryptographic_error";
    public const string OperationBusy = "operation_busy";
    public const string UnexpectedException = "unexpected_exception";
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.Http;
using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>
/// Stable failure tokens written to the result file and mapped to localized messages.
/// </summary>
public static class RegistrationFailureCodes
{
    /// <summary>The serial number or the hardware hash could not be read.</summary>
    public const string HardwareIdentityUnavailable = "hardware_identity_unavailable";

    /// <summary>Graph answered 401.</summary>
    public const string GraphUnauthorized = "graph_unauthorized";

    /// <summary>Graph answered 403: the account lacks a role that can import Autopilot devices.</summary>
    public const string GraphForbidden = "graph_forbidden";

    /// <summary>The device is registered in another tenant (<c>ZtdDeviceAssignedToOtherTenant</c>).</summary>
    public const string DeviceAssignedToOtherTenant = "device_assigned_to_other_tenant";

    /// <summary>The device is already registered (<c>ZtdDeviceDuplicated</c>).</summary>
    public const string DeviceDuplicated = "device_duplicated";

    /// <summary>Several Autopilot devices share the serial number.</summary>
    public const string DeviceAmbiguous = "device_ambiguous";

    /// <summary>The imported device did not become visible before the timeout.</summary>
    public const string RegistrationTimedOut = "registration_timed_out";

    /// <summary>Graph could not be reached.</summary>
    public const string NetworkUnavailable = "network_unavailable";

    /// <summary>The setup deadline was reached before registration finished.</summary>
    public const string DeadlineReached = "deadline_reached";

    /// <summary>Any other failure; details are in the log.</summary>
    public const string RegistrationFailed = "registration_failed";

    private const string ResourceKeyPrefix = "Registration.Failure.";

    /// <summary>
    /// Gets the resource key of the localized message for a code.
    /// </summary>
    /// <param name="code">One of the codes of this class.</param>
    /// <returns><c>Registration.Failure.&lt;code&gt;</c>.</returns>
    public static string ResourceKey(string code) => ResourceKeyPrefix + code;

    /// <summary>
    /// Maps an exception thrown while talking to Graph.
    /// </summary>
    /// <remarks>
    /// A <see cref="TaskCanceledException"/> reaching this method is an HTTP timeout: callers rethrow a cancellation they
    /// requested themselves before mapping.
    /// </remarks>
    /// <param name="exception">The failure.</param>
    /// <returns>A failure code.</returns>
    public static string FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => GraphUnauthorized,
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => GraphForbidden,
            HttpRequestException { StatusCode: null } => NetworkUnavailable,
            IOException or TimeoutException or TaskCanceledException => NetworkUnavailable,
            _ => RegistrationFailed
        };
    }

    /// <summary>
    /// Maps a failed upload result.
    /// </summary>
    /// <param name="result">The result returned by the Graph client.</param>
    /// <returns>A failure code.</returns>
    public static string FromResult(AutopilotHardwareHashUploadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Contains(result.ImportErrorName, "ZtdDeviceAssignedToOtherTenant"))
        {
            return DeviceAssignedToOtherTenant;
        }

        if (Contains(result.ImportErrorName, "ZtdDeviceDuplicated"))
        {
            return DeviceDuplicated;
        }

        if (string.Equals(result.FailureCode, "AutopilotDeviceAmbiguous", StringComparison.Ordinal))
        {
            return DeviceAmbiguous;
        }

        return result.State == AutopilotHardwareHashUploadState.UploadTimedOut ? RegistrationTimedOut : RegistrationFailed;
    }

    private static bool Contains(string? value, string token) =>
        value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
}

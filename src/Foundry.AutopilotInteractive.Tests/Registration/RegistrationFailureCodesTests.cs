// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class RegistrationFailureCodesTests
{
    [Fact]
    public void FromException_Maps401And403()
    {
        Assert.Equal(
            RegistrationFailureCodes.GraphUnauthorized,
            RegistrationFailureCodes.FromException(new HttpRequestException("x", null, HttpStatusCode.Unauthorized)));
        Assert.Equal(
            RegistrationFailureCodes.GraphForbidden,
            RegistrationFailureCodes.FromException(new HttpRequestException("x", null, HttpStatusCode.Forbidden)));
        Assert.Equal(
            RegistrationFailureCodes.RegistrationFailed,
            RegistrationFailureCodes.FromException(new HttpRequestException("x", null, HttpStatusCode.BadRequest)));
    }

    [Fact]
    public void FromException_MapsNetworkFailures()
    {
        Assert.Equal(RegistrationFailureCodes.NetworkUnavailable, RegistrationFailureCodes.FromException(new HttpRequestException("x")));
        Assert.Equal(RegistrationFailureCodes.NetworkUnavailable, RegistrationFailureCodes.FromException(new IOException("x")));
        Assert.Equal(
            RegistrationFailureCodes.NetworkUnavailable,
            RegistrationFailureCodes.FromException(new TaskCanceledException("x", new TimeoutException())));
        Assert.Equal(RegistrationFailureCodes.RegistrationFailed, RegistrationFailureCodes.FromException(new InvalidOperationException("x")));
    }

    [Fact]
    public void FromResult_MapsOtherTenantAndDuplicated()
    {
        Assert.Equal(
            RegistrationFailureCodes.DeviceAssignedToOtherTenant,
            RegistrationFailureCodes.FromResult(Failed(AutopilotHardwareHashUploadState.UploadFailed, importErrorName: "ZtdDeviceAssignedToOtherTenant")));
        Assert.Equal(
            RegistrationFailureCodes.DeviceDuplicated,
            RegistrationFailureCodes.FromResult(Failed(AutopilotHardwareHashUploadState.UploadFailed, importErrorName: "ZtdDeviceDuplicated")));
        Assert.Equal(
            RegistrationFailureCodes.RegistrationFailed,
            RegistrationFailureCodes.FromResult(Failed(AutopilotHardwareHashUploadState.UploadFailed, importErrorName: "Other")));
    }

    [Fact]
    public void FromResult_MapsAmbiguousAndTimeout()
    {
        Assert.Equal(
            RegistrationFailureCodes.DeviceAmbiguous,
            RegistrationFailureCodes.FromResult(Failed(AutopilotHardwareHashUploadState.UploadFailed, failureCode: "AutopilotDeviceAmbiguous")));
        Assert.Equal(
            RegistrationFailureCodes.RegistrationTimedOut,
            RegistrationFailureCodes.FromResult(Failed(AutopilotHardwareHashUploadState.UploadTimedOut, failureCode: "AutopilotDeviceTimedOut")));
    }

    [Fact]
    public void ResourceKey_PrefixesTheCode()
    {
        Assert.Equal("Registration.Failure.graph_forbidden", RegistrationFailureCodes.ResourceKey(RegistrationFailureCodes.GraphForbidden));
    }

    private static AutopilotHardwareHashUploadResult Failed(
        AutopilotHardwareHashUploadState state,
        string? failureCode = null,
        string? importErrorName = null) =>
        AutopilotHardwareHashUploadResult.Failed(state, "message", failureCode, importErrorName: importErrorName);
}

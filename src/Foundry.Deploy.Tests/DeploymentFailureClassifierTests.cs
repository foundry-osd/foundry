// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentFailureClassifierTests
{
    [Fact]
    public void Classify_PreservesStructuredFailure()
    {
        var expected = new DeploymentFailure(
            "boot.configure",
            DeploymentFailureKinds.Process,
            DeploymentFailureReasons.NonZeroExit,
            "-193");

        DeploymentFailure actual = DeploymentFailureClassifier.Classify(
            new DeploymentOperationException(expected, "BCDBoot failed."),
            "fallback.operation");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Classify_UsesHttpStatusWithoutIncludingExceptionMessage()
    {
        DeploymentFailure failure = DeploymentFailureClassifier.Classify(
            new HttpRequestException(
                @"Sensitive URL C:\private",
                inner: null,
                HttpStatusCode.BadGateway),
            "os_image.download");

        Assert.Equal("os_image.download", failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Http, failure.Kind);
        Assert.Equal(DeploymentFailureReasons.HttpStatus, failure.Reason);
        Assert.Equal("502", failure.Code);
    }

    [Fact]
    public void Classify_UsesProcessExitCodeWithoutIncludingExceptionMessage()
    {
        DeploymentFailure failure = DeploymentFailureClassifier.Classify(
            new DeploymentProcessException(@"Sensitive path C:\private", -193),
            "boot.configure");

        Assert.Equal("boot.configure", failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Process, failure.Kind);
        Assert.Equal(DeploymentFailureReasons.NonZeroExit, failure.Reason);
        Assert.Equal("-193", failure.Code);
    }

    [Fact]
    public void Classify_MapsProcessStartFailureWithoutIncludingExceptionMessage()
    {
        DeploymentFailure failure = DeploymentFailureClassifier.Classify(
            new ProcessStartException(@"Sensitive path C:\private"),
            "driver_pack.extract");

        Assert.Equal("driver_pack.extract", failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Process, failure.Kind);
        Assert.Equal(DeploymentFailureReasons.StartFailed, failure.Reason);
        Assert.Null(failure.Code);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), "io", "not_found")]
    [InlineData(typeof(UnauthorizedAccessException), "io", "access_denied")]
    [InlineData(typeof(TimeoutException), "timeout", "deadline_exceeded")]
    [InlineData(typeof(CryptographicException), "cryptography", "cryptographic_error")]
    [InlineData(typeof(InvalidDataException), "validation", "invalid_payload")]
    [InlineData(typeof(InvalidOperationException), "validation", "invalid_state")]
    [InlineData(typeof(Exception), "unexpected", "unexpected_exception")]
    public void Classify_MapsKnownExceptionFamilies(Type exceptionType, string expectedKind, string expectedReason)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "Sensitive message")!;

        DeploymentFailure failure = DeploymentFailureClassifier.Classify(exception, "deployment.operation");

        Assert.Equal("deployment.operation", failure.OperationName);
        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal(expectedReason, failure.Reason);
        Assert.Null(failure.Code);
    }
}

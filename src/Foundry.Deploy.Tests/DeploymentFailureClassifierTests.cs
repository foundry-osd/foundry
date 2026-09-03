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
    public void DeploymentOperationException_PreservesInnerException()
    {
        var inner = new IOException("Disk failed.");
        var failure = new DeploymentFailure(
            "disk.prepare",
            DeploymentFailureKinds.Io,
            DeploymentFailureReasons.UnexpectedException);

        var exception = new DeploymentOperationException(failure, "Preparation failed.", inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Equal(failure, DeploymentFailureClassifier.Classify(exception, "fallback"));
    }

    [Theory]
    [InlineData(DeploymentFailureReasons.MissingResource, "missing_target_partition")]
    [InlineData(DeploymentFailureReasons.InvalidInput, "unsupported_driver_mode")]
    [InlineData(DeploymentFailureReasons.NotFound, "autopilot_profile_file_not_found")]
    public void Guard_UsesExplicitStableReasonAndCode(string reason, string code)
    {
        DeploymentFailure failure = DeploymentFailure.Guard("deployment.step", reason, code);

        Assert.Equal("deployment.step", failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Validation, failure.Kind);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(code, failure.Code);
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
    public async Task Classify_MapsProcessStartFailureWithoutIncludingExceptionMessage()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory("FoundryDeployFailureClassifier-");
        try
        {
            string executablePath = Path.Combine(workspace.FullName, "missing.exe");
            var request = new Foundry.Utilities.Processes.ProcessExecutionRequest(
                executablePath,
                [],
                workspace.FullName);
            Foundry.Utilities.Processes.ProcessStartException exception = await Assert.ThrowsAsync<Foundry.Utilities.Processes.ProcessStartException>(() =>
                new Foundry.Utilities.Processes.ProcessRunner()
                    .RunAsync(request, TestContext.Current.CancellationToken));

            DeploymentFailure failure = DeploymentFailureClassifier.Classify(exception, "driver_pack.extract");

            Assert.Equal("driver_pack.extract", failure.OperationName);
            Assert.Equal(DeploymentFailureKinds.Process, failure.Kind);
            Assert.Equal(DeploymentFailureReasons.StartFailed, failure.Reason);
            Assert.NotNull(failure.Code);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), "io", "not_found")]
    [InlineData(typeof(UnauthorizedAccessException), "io", "access_denied")]
    [InlineData(typeof(TaskCanceledException), "timeout", "deadline_exceeded")]
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

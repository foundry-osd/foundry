// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeProcessExecutionTests
{
    [Fact]
    public void ToDiagnosticText_IncludesCommandWorkingDirectoryExitCodeAndStreams()
    {
        var execution = new WinPeProcessExecution
        {
            FileName = "dism.exe",
            Arguments = "/?",
            WorkingDirectory = "C:\\Work",
            ExitCode = 1,
            StandardOutput = "output",
            StandardError = "error"
        };

        Assert.Equal(
            "Command: dism.exe /?\r\n" +
            "WorkingDirectory: C:\\Work\r\n" +
            "ExitCode: 1\r\n" +
            "StdOut:\r\n" +
            "output\r\n" +
            "StdErr:\r\n" +
            "error",
            execution.ToDiagnosticText());
    }

    [Fact]
    public void ToFailureDiagnostic_PreservesLocalDetailsAndProcessMetadata()
    {
        var execution = new WinPeProcessExecution
        {
            FileName = @"C:\Program Files\Windows Kits\MakeWinPEMedia.cmd",
            Arguments = "/ISO secret=plain-text",
            WorkingDirectory = @"C:\Users\operator\workspace",
            ExitCode = 7,
            StandardOutput = "raw output",
            StandardError = @"token=plain-text C:\Users\operator\private.txt"
        };

        WinPeDiagnostic diagnostic = execution.ToFailureDiagnostic(
            WinPeErrorCodes.IsoCreateFailed,
            "Failed to create WinPE ISO media.",
            "Create ISO media",
            "MakeWinPEMedia");

        Assert.Equal(WinPeFailureKinds.Process, diagnostic.FailureKind);
        Assert.Equal(WinPeFailureReasons.NonZeroExit, diagnostic.FailureReason);
        Assert.Equal("MakeWinPEMedia", diagnostic.ToolName);
        Assert.Equal(7, diagnostic.ExitCode);
        Assert.Contains("plain-text", diagnostic.Details);
    }

    [Theory]
    [InlineData("dism.exe", 5, "", "Fehler: 5\r\nZugriff verweigert", WinPeFailureReasons.AccessDenied)]
    [InlineData("dism.exe", 2, "", "", WinPeFailureReasons.NonZeroExit)]
    [InlineData("MakeWinPEMedia", 5, "", "", WinPeFailureReasons.NonZeroExit)]
    [InlineData("copype", 1, "ERROR: Failed to mount the WinPE WIM file. Check logs at C:\\WINDOWS\\Logs\\DISM", "Zugriff verweigert", WinPeFailureReasons.AccessDenied)]
    [InlineData("copype", 1, "ERROR: Unable to copy boot file: \"bootmgfw.efi\" to \"C:\\Work\\WinPe\\bootbins\".", "Ce fichier n'est actuellement pas disponible.", WinPeFailureReasons.AccessDenied)]
    [InlineData("copype", 1, "ERROR: The following processor architecture was not found: arm.", "", WinPeFailureReasons.NonZeroExit)]
    public void ToFailureDiagnostic_ClassifiesBlockedFileAccessFromStableToolSignals(
        string toolName,
        int exitCode,
        string standardOutput,
        string standardError,
        string expectedReason)
    {
        var execution = new WinPeProcessExecution
        {
            FileName = toolName,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError
        };

        WinPeDiagnostic diagnostic = execution.ToFailureDiagnostic(
            WinPeErrorCodes.BuildFailed,
            "WinPE build step failed.",
            "Build WinPE workspace",
            toolName);

        Assert.Equal(WinPeErrorCodes.BuildFailed, diagnostic.Code);
        Assert.Equal(WinPeFailureKinds.Process, diagnostic.FailureKind);
        Assert.Equal(expectedReason, diagnostic.FailureReason);
        Assert.Equal(exitCode, diagnostic.ExitCode);
    }

    [Theory]
    [InlineData("copype", "Mounting \"C:\\Work\\WinPe\\media\\sources\\boot.wim\"\r\nERROR: Failed to mount the WinPE WIM file. Check logs at C:\\WINDOWS\\Logs\\DISM for more details.", "Zugriff verweigert", WinPeCopypeFailureDetails.WimMountFailed)]
    [InlineData("copype", "ERROR: Unable to copy boot file: \"bootmgfw.efi\" to \"C:\\Work\\WinPe\\bootbins\".", "This file is currently not available for use on this computer.", WinPeCopypeFailureDetails.BootFileCopyFailed)]
    [InlineData("copype", "ERROR: Unable to copy boot files: \"C:\\ADK\\amd64\\Media\" to \"C:\\Work\\WinPe\\media\".", "", WinPeCopypeFailureDetails.MediaCopyFailed)]
    [InlineData("copype", "ERROR: Unable to copy boot sector file: \"C:\\ADK\\Oscdimg\\efisys.bin\" to \"C:\\Work\\WinPe\\bootbins\".", "", WinPeCopypeFailureDetails.BootSectorFileCopyFailed)]
    [InlineData("copype", "ERROR: Destination directory exists: C:\\Work\\WinPe.", "", WinPeCopypeFailureDetails.DestinationExists)]
    [InlineData("copype", "ERROR: Failed to mount the WinPE WIM file.\r\nERROR: \"C:\\Work\\WinPe\\media\\sources\\boot.wim\" still mounted!", "", WinPeCopypeFailureDetails.WimMountFailed)]
    [InlineData("copype", "Staging media files...", "", WinPeCopypeFailureDetails.Unrecognized)]
    [InlineData("copype", "", "", WinPeCopypeFailureDetails.NoOutput)]
    [InlineData("dism.exe", "ERROR: Failed to mount the WinPE WIM file.", "", null)]
    public void ToFailureDiagnostic_IdentifiesFailedCopypeStepFromScriptErrorLines(
        string toolName,
        string standardOutput,
        string standardError,
        string? expectedDetail)
    {
        var execution = new WinPeProcessExecution
        {
            FileName = "cmd.exe",
            ExitCode = 1,
            StandardOutput = standardOutput,
            StandardError = standardError
        };

        WinPeDiagnostic diagnostic = execution.ToFailureDiagnostic(
            WinPeErrorCodes.BuildFailed,
            "WinPE build step failed.",
            "Build WinPE workspace",
            toolName);

        Assert.Equal(expectedDetail, diagnostic.FailureDetail);
    }

    [Fact]
    public void Failure_WithException_PreservesOriginalExceptionAndClassification()
    {
        var exception = new UnauthorizedAccessException("Access denied.");

        WinPeResult result = WinPeResult.Failure(
            WinPeErrorCodes.IsoCreateFailed,
            "ISO creation failed.",
            exception.Message,
            exception: exception);

        Assert.Same(exception, result.Error!.Exception);
        Assert.Equal(WinPeFailureKinds.FileSystem, result.Error.FailureKind);
        Assert.Equal(WinPeFailureReasons.AccessDenied, result.Error.FailureReason);
    }

    [Fact]
    public void Diagnostic_ClassifiesCommonFailureBoundaries()
    {
        WinPeDiagnostic httpStatus = CreateDiagnostic(
            new HttpRequestException("Not found.", null, System.Net.HttpStatusCode.NotFound));
        WinPeDiagnostic transport = CreateDiagnostic(new HttpRequestException("Connection reset."));
        WinPeDiagnostic timeout = CreateDiagnostic(new TimeoutException("Timed out."));
        WinPeDiagnostic cancelled = CreateDiagnostic(new OperationCanceledException("Cancelled."));
        var disk = new WinPeDiagnostic(WinPeErrorCodes.UsbUnsafeTarget, "Unsafe disk.");

        Assert.Equal((WinPeFailureKinds.Network, WinPeFailureReasons.HttpStatus), (httpStatus.FailureKind, httpStatus.FailureReason));
        Assert.Equal((WinPeFailureKinds.Network, WinPeFailureReasons.Transport), (transport.FailureKind, transport.FailureReason));
        Assert.Equal((WinPeFailureKinds.Network, WinPeFailureReasons.Timeout), (timeout.FailureKind, timeout.FailureReason));
        Assert.Equal((WinPeFailureKinds.Cancellation, WinPeFailureReasons.Cancelled), (cancelled.FailureKind, cancelled.FailureReason));
        Assert.Equal((WinPeFailureKinds.Validation, WinPeFailureReasons.DiskValidation), (disk.FailureKind, disk.FailureReason));
    }

    private static WinPeDiagnostic CreateDiagnostic(Exception exception) => new(
        WinPeErrorCodes.DownloadFailed,
        "Operation failed.",
        exception: exception);

    [Theory]
    [InlineData("C:\\Tools\\copype.cmd", "C:\\Tools\\copype.cmd")]
    [InlineData("C:\\Program Files\\copype.cmd", "\"C:\\Program Files\\copype.cmd\"")]
    public void Quote_QuotesOnlyWhenValueContainsSpaces(string value, string expected)
    {
        Assert.Equal(expected, WinPeProcessRunner.Quote(value));
    }
}

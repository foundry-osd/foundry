// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDiagnosticTests
{
    [Theory]
    [InlineData("WINPE_LOCAL_SPACE_INSUFFICIENT")]
    [InlineData("WINPE_LOCAL_SPACE_UNKNOWN")]
    public void Constructor_WhenLocalSpaceValidationFails_ClassifiesDiskValidation(string code)
    {
        var diagnostic = new WinPeDiagnostic(code, "Local storage validation failed.");

        Assert.Equal(WinPeFailureKinds.Validation, diagnostic.FailureKind);
        Assert.Equal(WinPeFailureReasons.DiskValidation, diagnostic.FailureReason);
    }

    [Fact]
    public void Constructor_WhenOperationIsCancelled_ClassifiesCancellation()
    {
        var diagnostic = new WinPeDiagnostic(WinPeErrorCodes.OperationCancelled, "Operation cancelled.");

        Assert.Equal(WinPeFailureKinds.Cancellation, diagnostic.FailureKind);
        Assert.Equal(WinPeFailureReasons.Cancelled, diagnostic.FailureReason);
    }
}

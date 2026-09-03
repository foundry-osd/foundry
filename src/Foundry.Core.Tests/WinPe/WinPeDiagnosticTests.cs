// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDiagnosticTests
{
    [Fact]
    public void Constructor_WhenOperationIsCancelled_ClassifiesCancellation()
    {
        var diagnostic = new WinPeDiagnostic(WinPeErrorCodes.OperationCancelled, "Operation cancelled.");

        Assert.Equal(WinPeFailureKinds.Cancellation, diagnostic.FailureKind);
        Assert.Equal(WinPeFailureReasons.Cancelled, diagnostic.FailureReason);
    }
}

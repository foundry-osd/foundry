// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class DiagnosticSessionContextTests
{
    [Fact]
    public void ResolveSessionId_NormalizesInheritedValue()
    {
        string result = DiagnosticSessionContext.ResolveSessionId(" boot\r\nsession\t01 ");

        Assert.Equal("boot-session-01", result);
    }

    [Fact]
    public void ResolveSessionId_WhenValueIsMissing_CreatesShortIdentifier()
    {
        string result = DiagnosticSessionContext.ResolveSessionId(null);

        Assert.Matches("^[A-F0-9]{8}$", result);
    }

    [Fact]
    public void ResolveSessionId_NeverExceedsMaximumLengthWhenSeparatorIsInserted()
    {
        string inheritedValue = new string('A', 31) + " B";

        string result = DiagnosticSessionContext.ResolveSessionId(inheritedValue);

        Assert.InRange(result.Length, 1, 32);
        Assert.DoesNotContain(' ', result);
    }
}

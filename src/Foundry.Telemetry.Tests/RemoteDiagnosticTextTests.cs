// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry.Tests;

public sealed class RemoteDiagnosticTextTests
{
    [Theory]
    [InlineData("Cannot read C:\\private folder\\image.wim because the device is not ready.")]
    [InlineData("Cannot read 'C:\\private folder\\image.wim' because the device is not ready.")]
    [InlineData("Cannot read \\\\private-server\\share\\image.wim because the device is not ready.")]
    public void Sanitize_PreservesExplanationAfterPath(string message)
    {
        string result = RemoteDiagnosticText.Sanitize(message, 2048);

        Assert.Contains("because the device is not ready", result);
        Assert.DoesNotContain("private", result);
        Assert.DoesNotContain("image.wim", result);
    }

    [Theory]
    [InlineData("Authorization: Basic secret\nError: 21")]
    [InlineData("Authorization: Bearer secret\nError: 21")]
    [InlineData("{\"Password\":\"secret\"}\nError: 21")]
    [InlineData("<Password><Value>secret</Value></Password>\nError: 21")]
    [InlineData("https://host.example/path?token=secret\nError: 21")]
    public void SanitizeOutput_RemovesCredentialsButKeepsError(string output)
    {
        string result = RemoteDiagnosticText.SanitizeOutput(output);

        Assert.DoesNotContain("secret", result);
        Assert.Contains("Error: 21", result);
    }

    [Fact]
    public void SanitizeOutput_RedactsBeforeSelectingTail()
    {
        string output = "Password=\"" + new string('s', 18000) + "\"\nError: 21";

        string result = RemoteDiagnosticText.SanitizeOutput(output);

        Assert.DoesNotContain(new string('s', 20), result);
        Assert.Contains("Error: 21", result);
    }
}

// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class AutopilotProfileImportServiceTests
{
    [Fact]
    public async Task Import_ValidManualAscii_PreservesEveryByteIncludingWhitespace()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = Path.Combine(tempDirectory.Path, "profile.json");
        string original = " \r\n" + AutopilotOfflineProfileTestData.ValidJson + "\r\n  ";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var result = await new AutopilotProfileImportService().ImportFromJsonFileAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(original, result.JsonContent);
    }

    [Fact]
    public async Task Import_UnicodeEncodingMarker_IsRejectedInsteadOfSilentlyChangingBytes()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = Path.Combine(tempDirectory.Path, "profile.json");
        await File.WriteAllTextAsync(path, AutopilotOfflineProfileTestData.ValidJson, System.Text.Encoding.Unicode, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => new AutopilotProfileImportService().ImportFromJsonFileAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"Comment_File\": \"Crème\" }")]
    public async Task ImportFromJsonFileAsync_WhenContentIsInvalid_DoesNotExposeSourcePath(string content)
    {
        using var tempDirectory = new TemporaryDirectory();
        string sourcePath = Path.Combine(tempDirectory.Path, "customer-profile.json");
        await File.WriteAllTextAsync(sourcePath, content, TestContext.Current.CancellationToken);
        var service = new AutopilotProfileImportService();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.ImportFromJsonFileAsync(sourcePath, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(tempDirectory.Path, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer-profile.json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

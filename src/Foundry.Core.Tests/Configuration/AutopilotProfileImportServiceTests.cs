// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class AutopilotProfileImportServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{ \"Comment_File\": \"Crème\" }")]
    public async Task ImportFromJsonFileAsync_WhenContentIsInvalid_DoesNotExposeSourcePath(string content)
    {
        using var tempDirectory = new TemporaryDirectory();
        string sourcePath = Path.Combine(tempDirectory.Path, "customer-profile.json");
        await File.WriteAllTextAsync(sourcePath, content, TestContext.Current.CancellationToken);
        var service = new AutopilotProfileImportService();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportFromJsonFileAsync(sourcePath, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(tempDirectory.Path, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer-profile.json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

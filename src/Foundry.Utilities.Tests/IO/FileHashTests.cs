// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class FileHashTests
{
    [Fact]
    public async Task ComputeSha256Async_ComputesKnownTextVector()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "input.txt");
        await File.WriteAllTextAsync(filePath, "abc", TestContext.Current.CancellationToken);

        string hash = await FileHash.ComputeSha256Async(filePath, CancellationToken.None);

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", hash);
    }

    [Fact]
    public async Task ComputeSha256Async_ComputesBinaryFileHash()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "input.bin");
        await File.WriteAllBytesAsync(filePath, [0x00, 0x01, 0x02, 0xFF], TestContext.Current.CancellationToken);

        string hash = await FileHash.ComputeSha256Async(filePath, CancellationToken.None);

        Assert.Equal("3D1F57C984978EF98A18378C8166C1CB8EDE02C03EEB6AEE7E2F121DFEEE3E56", hash);
    }

    [Fact]
    public async Task ComputeSha256Async_WhenCancellationIsRequested_ThrowsOperationCanceledException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "input.txt");
        await File.WriteAllTextAsync(filePath, "content", TestContext.Current.CancellationToken);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileHash.ComputeSha256Async(filePath, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ComputeSha256Async_WhenFileIsMissing_ThrowsFileNotFoundException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "missing.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => FileHash.ComputeSha256Async(filePath, CancellationToken.None));
    }
}

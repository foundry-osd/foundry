// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Deploy.Services.Images;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageSourceLeaseTests
{
    [Fact]
    public async Task AcquireAsync_ProtectsFileBeforeInspectionAndRejectsDigestMismatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "image.wim");
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => CustomImageSourceLease.AcquireAsync(
                path, 3, new string('0', 64), TestContext.Current.CancellationToken));
            using var lease = await CustomImageSourceLease.AcquireAsync(path, 3,
                Convert.ToHexString(SHA256.HashData([1, 2, 3])), TestContext.Current.CancellationToken);
            Assert.Throws<IOException>(() => File.WriteAllBytes(path, [3, 2, 1]));
            Assert.Throws<IOException>(() => File.Delete(path));
            Assert.Equal(3, lease.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AcquireAsync_UnmanagedFileRecordsActualDigestAndReleasesLock()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "image.wim");
        try
        {
            await File.WriteAllBytesAsync(path, [9, 8], TestContext.Current.CancellationToken);
            using (var lease = await CustomImageSourceLease.AcquireAsync(path, null, null, TestContext.Current.CancellationToken))
                Assert.Equal(Convert.ToHexString(SHA256.HashData([9, 8])), lease.ContentHash);
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}

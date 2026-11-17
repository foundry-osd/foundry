// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PostInstallRuntimeSourceTests
{
    [Fact]
    public async Task BootImageRuntime_IsVerifiedAndLockedUntilDisposedWithoutDeletingIt()
    {
        using var fixture = new Fixture();
        using (var prepared = await PostInstallRuntimeSource.AcquireAsync(fixture.Directory, "win-x64", TestContext.Current.CancellationToken))
        {
            Assert.Equal(fixture.Directory, prepared.RuntimeDirectory);
            Assert.Equal(fixture.Manifest.Files.Sum(file => file.Length), prepared.TargetBytes);
            foreach (string path in Directory.GetFiles(fixture.Directory))
            {
                Assert.Throws<IOException>(() => File.Delete(path));
                Assert.Throws<IOException>(() => File.WriteAllText(path, "replacement"));
            }
        }
        Assert.True(File.Exists(fixture.Executable));
        File.WriteAllText(fixture.Executable, "replacement after release");
    }

    [Theory]
    [InlineData(2, 1, "win-x64")]
    [InlineData(1, 2, "win-x64")]
    [InlineData(1, 1, "win-arm64")]
    public async Task IncompatibleBootImageRuntime_IsRejected(int schema, int contract, string rid)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { SchemaVersion = schema, ContractVersion = contract, RuntimeIdentifier = rid });
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(() => AcquireAsync(fixture));
    }

    [Theory]
    [InlineData("v26.9.1.1")]
    [InlineData(null)]
    public async Task ReleaseTag_IsInformational(string? releaseTag)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { ReleaseTag = releaseTag });
        using var prepared = await AcquireAsync(fixture);
        Assert.Equal(releaseTag, prepared.RuntimeManifest.ReleaseTag);
    }

    [Theory]
    [InlineData("Foundry.PostInstall.exe", false)]
    [InlineData("Launch.cmd", false)]
    [InlineData("Foundry.PostInstall.exe", true)]
    [InlineData("Launch.cmd", true)]
    public async Task TamperedRuntime_IsRejectedAndReleasesOpenedFiles(string name, bool truncated)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Directory, name);
        byte[] bytes = File.ReadAllBytes(path);
        if (truncated) bytes = bytes[..^1];
        else bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes);
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(() => AcquireAsync(fixture));
        foreach (string file in Directory.GetFiles(fixture.Directory)) File.Delete(file);
    }

    [Theory]
    [InlineData("Foundry.PostInstall.exe")]
    [InlineData("Launch.cmd")]
    [InlineData(PostInstallRuntimeManifest.FileName)]
    public async Task MissingRuntimeFile_IsRejected(string name)
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Directory, name));
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(() => AcquireAsync(fixture));
    }

    [Theory]
    [InlineData("Foundry.PostInstall.exe")]
    [InlineData("Launch.cmd")]
    public async Task ManifestOmittingRequiredFile_IsRejected(string name)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { Files = fixture.Manifest.Files.Where(file => file.RelativePath != name).ToArray() });
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(() => AcquireAsync(fixture));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("NUL")]
    [InlineData("Foundry.PostInstall.exe")]
    public async Task UnsafeOrDuplicateManifestPath_IsRejected(string path)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { Files = [.. fixture.Manifest.Files, fixture.Manifest.Files[0] with { RelativePath = path }] });
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(() => AcquireAsync(fixture));
    }

    [Fact]
    public async Task MissingBootImageDirectory_IsRejected()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"), "PostInstall");
        var exception = await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(
            () => PostInstallRuntimeSource.AcquireAsync(directory, "win-x64", TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<IOException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PostInstall")]
    public async Task RelativeRuntimeDirectory_IsRejected(string directory)
    {
        await Assert.ThrowsAsync<PostInstallRuntimeMissingException>(
            () => PostInstallRuntimeSource.AcquireAsync(directory, "win-x64", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task TargetCopy_RejectsOverlongAndTruncatedContent(long expected)
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.CopyBoundedAsync(input, output, expected, TestContext.Current.CancellationToken));
        Assert.True(output.Length <= expected);
    }

    private static Task<PreOobePreparedContent> AcquireAsync(Fixture fixture) =>
        PostInstallRuntimeSource.AcquireAsync(fixture.Directory, "win-x64", TestContext.Current.CancellationToken);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public string Directory { get; }
        public string Executable => Path.Combine(Directory, "Foundry.PostInstall.exe");
        public PostInstallRuntimeManifest Manifest => JsonSerializer.Deserialize<PostInstallRuntimeManifest>(
            File.ReadAllText(Path.Combine(Directory, PostInstallRuntimeManifest.FileName)), ConfigurationJsonDefaults.SerializerOptions)!;
        public Fixture() => Directory = NativeRuntimeFixture.CreateFiles(_root);
        public void Write(PostInstallRuntimeManifest manifest) => NativeRuntimeFixture.WriteManifest(Directory, manifest);
        public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
    }
}

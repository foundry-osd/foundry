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
    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(3, 2, false)]
    public async Task RuntimeMustMeetEffectiveCapability(int contract, int required, bool accepted)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { ContractVersion = contract });
        if (accepted)
        {
            using var prepared = await PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken, required);
            Assert.Equal(contract, prepared.RuntimeManifest.ContractVersion);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken, required));
    }

    [Fact]
    public async Task CurrentRuntimeSupportsLegacyDeployment()
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { ContractVersion = 2 });
        using var prepared = await PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken);
        Assert.Equal(2, prepared.RuntimeManifest.ContractVersion);
    }

    [Fact]
    public async Task BootstrapHandoff_VerifiesAndLocksFilesUntilDisposedWithoutDeletingRuntime()
    {
        using var fixture = new Fixture();
        using (var prepared = await PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken))
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
    [InlineData(1, 3, "win-x64")]
    [InlineData(1, 1, "win-arm64")]
    public async Task IncompatibleBootstrapRuntime_IsRejected(int schema, int contract, string rid)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { SchemaVersion = schema, ContractVersion = contract, RuntimeIdentifier = rid });
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken));
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
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken));
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
        await Assert.ThrowsAnyAsync<IOException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Foundry.PostInstall.exe")]
    [InlineData("Launch.cmd")]
    public async Task ManifestOmittingRequiredFile_IsRejected(string name)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { Files = fixture.Manifest.Files.Where(file => file.RelativePath != name).ToArray() });
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("NUL")]
    [InlineData("Foundry.PostInstall.exe")]
    public async Task UnsafeOrDuplicateManifestPath_IsRejected(string path)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Manifest with { Files = [.. fixture.Manifest.Files, fixture.Manifest.Files[0] with { RelativePath = path }] });
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(fixture.Executable, "win-x64", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingBootstrapHandoff_IsRejected(string? executable)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => PostInstallRuntimeSource.AcquireAsync(executable, "win-x64", TestContext.Current.CancellationToken));
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

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public string Executable { get; }
        public string Directory => Path.GetDirectoryName(Executable)!;
        public PostInstallRuntimeManifest Manifest => JsonSerializer.Deserialize<PostInstallRuntimeManifest>(
            File.ReadAllText(Path.Combine(Directory, PostInstallRuntimeManifest.FileName)), ConfigurationJsonDefaults.SerializerOptions)!;
        public Fixture() => Executable = NativeRuntimeFixture.CreateFiles(_root);
        public void Write(PostInstallRuntimeManifest manifest) => NativeRuntimeFixture.WriteManifest(Directory, manifest);
        public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
    }
}

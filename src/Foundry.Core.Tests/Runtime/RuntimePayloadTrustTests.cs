// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimePayloadTrustTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"foundry-trust-{Guid.NewGuid():N}");
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public void ReadArchiveHash_ResolvesOnlyMatchingApplicationAndRuntime(string runtime)
    {
        WriteManifest($$"""{"schemaVersion":1,"archives":[{"application":"Foundry.PostInstall","runtimeIdentifier":"{{runtime}}","sha256":"{{Hash}}"}]}""");
        Assert.Equal(Hash, RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.PostInstall", runtime));
        Assert.Null(RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.AutopilotInteractive", runtime));
        Assert.Equal(Path.Combine(_root, "Foundry.PostInstall", runtime, "original.zip"), RuntimePayloadTrust.GetBaselineArchivePath(_root, "Foundry.PostInstall", runtime));
    }

    [Fact]
    public void ReadArchiveHash_WhenManifestIsMissing_ReturnsNull()
    {
        Assert.Null(RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.PostInstall", "win-x64"));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"archives\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"archives\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"../evil\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.PostInstall\",\"runtimeIdentifier\":\"../evil\",\"sha256\":\"HASH\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.PostInstall\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"invalid\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.PostInstall\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"},{\"application\":\"Foundry.PostInstall\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"}]}")]
    public void ReadArchiveHash_WhenManifestIsInvalid_FailsClosed(string json)
    {
        WriteManifest(json.Replace("HASH", Hash, StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.AutopilotInteractive", "win-x64"));
    }

    [Theory]
    [InlineData("Foundry.Connect")]
    [InlineData("Foundry.Deploy")]
    [InlineData("Foundry.WinPE")]
    public void GetBaselineArchivePath_WhenApplicationRunsFromTheBootImage_Rejects(string application)
    {
        Assert.Throws<InvalidDataException>(() => RuntimePayloadTrust.GetBaselineArchivePath(_root, application, "win-x64"));
    }

    [Fact]
    public void GetBaselineArchivePath_AcceptsAutopilotInteractive()
    {
        string path = RuntimePayloadTrust.GetBaselineArchivePath(Path.Combine(_root, "Runtime"), "Foundry.AutopilotInteractive", "win-x64");
        Assert.EndsWith(Path.Combine("Runtime", "Foundry.AutopilotInteractive", "win-x64", "original.zip"), path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../evil", "win-x64")]
    [InlineData("Foundry.PostInstall", "../evil")]
    public void GetBaselineArchivePath_WhenIdentityIsInvalid_Rejects(string application, string runtime)
    {
        Assert.Throws<InvalidDataException>(() => RuntimePayloadTrust.GetBaselineArchivePath(_root, application, runtime));
    }

    private void WriteManifest(string json)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Config"));
        File.WriteAllText(Path.Combine(_root, "Config", "foundry.runtime-trust.json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

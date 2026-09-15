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
        WriteManifest($$"""{"schemaVersion":1,"archives":[{"application":"Foundry.Connect","runtimeIdentifier":"{{runtime}}","sha256":"{{Hash}}"}]}""");
        Assert.Equal(Hash, RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.Connect", runtime));
        Assert.Null(RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.Deploy", runtime));
        Assert.Equal(Path.Combine(_root, "Foundry.Connect", runtime, "original.zip"), RuntimePayloadTrust.GetBaselineArchivePath(_root, "Foundry.Connect", runtime));
    }

    [Fact]
    public void ReadArchiveHash_WhenManifestIsMissing_ReturnsNull()
    {
        Assert.Null(RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.Connect", "win-x64"));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"archives\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"archives\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"../evil\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.Connect\",\"runtimeIdentifier\":\"../evil\",\"sha256\":\"HASH\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.Connect\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"invalid\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"archives\":[{\"application\":\"Foundry.Connect\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"},{\"application\":\"Foundry.Connect\",\"runtimeIdentifier\":\"win-x64\",\"sha256\":\"HASH\"}]}")]
    public void ReadArchiveHash_WhenManifestIsInvalid_FailsClosed(string json)
    {
        WriteManifest(json.Replace("HASH", Hash, StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => RuntimePayloadTrust.ReadArchiveHash(_root, "Foundry.Deploy", "win-x64"));
    }

    [Theory]
    [InlineData("../evil", "win-x64")]
    [InlineData("Foundry.Connect", "../evil")]
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

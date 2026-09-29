// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimeReleaseAssetTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("win-x64", "https://example.com/x64.zip")]
    [InlineData("win-arm64", "https://example.com/arm64.zip")]
    public void Parse_SelectsMatchingApplicationAndArchitecture(string runtime, string expectedUrl)
    {
        using JsonDocument release = JsonDocument.Parse($$"""
            {"assets":[
                {"name":"Foundry.Deploy-win-x64.zip","digest":"sha256:{{Hash}}","browser_download_url":"https://example.com/deploy.zip"},
                {"name":"Foundry.PostInstall-win-x64.zip","digest":"SHA256:{{Hash.ToUpperInvariant()}}","browser_download_url":"https://example.com/x64.zip"},
                {"name":"Foundry.PostInstall-win-arm64.zip","digest":"sha256:{{Hash}}","browser_download_url":"https://example.com/arm64.zip"}
            ]}
            """);

        RuntimeReleaseAsset asset = RuntimeReleaseAsset.Parse(release.RootElement, "Foundry.PostInstall", runtime);

        Assert.Equal(expectedUrl, asset.DownloadUrl);
        Assert.Equal(Hash, asset.Sha256);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"assets\":{}}")]
    [InlineData("{\"assets\":[]}")]
    [InlineData("{\"assets\":[null]}")]
    [InlineData("{\"assets\":[{}]}")]
    [InlineData("{\"assets\":[{\"name\":42}]}")]
    [InlineData("{\"assets\":[{\"name\":\"Foundry.PostInstall-win-arm64.zip\"}]}")]
    [InlineData("{\"assets\":[{\"name\":\"Foundry.PostInstall-win-x64.zip\"},{\"name\":\"Foundry.PostInstall-win-x64.zip\"}]}")]
    public void Parse_WhenAssetsAreMalformedMissingOrDuplicated_Rejects(string json)
    {
        using JsonDocument release = JsonDocument.Parse(json);

        Assert.Throws<InvalidDataException>(() => RuntimeReleaseAsset.Parse(release.RootElement, "Foundry.PostInstall", "win-x64"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:abc")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void Parse_WhenDigestIsMissingOrInvalid_Rejects(string? digest)
    {
        using JsonDocument release = CreateRelease(digest, "https://example.com/runtime.zip");

        Assert.Throws<InvalidDataException>(() => RuntimeReleaseAsset.Parse(release.RootElement, "Foundry.PostInstall", "win-x64"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/runtime.zip")]
    [InlineData("http://example.com/runtime.zip")]
    [InlineData("file:///runtime.zip")]
    public void Parse_WhenDownloadUrlIsMissingOrNotHttps_Rejects(string? url)
    {
        using JsonDocument release = CreateRelease("sha256:" + Hash, url);

        Assert.Throws<InvalidDataException>(() => RuntimeReleaseAsset.Parse(release.RootElement, "Foundry.PostInstall", "win-x64"));
    }

    [Theory]
    [InlineData("../evil", "win-x64")]
    [InlineData("Foundry.PostInstall", "../evil")]
    public void Parse_WhenIdentityIsUnsupported_Rejects(string application, string runtime)
    {
        using JsonDocument release = CreateRelease("sha256:" + Hash, "https://example.com/runtime.zip");

        Assert.Throws<InvalidDataException>(() => RuntimeReleaseAsset.Parse(release.RootElement, application, runtime));
    }

    private static JsonDocument CreateRelease(string? digest, string? url) => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        assets = new[] { new { name = "Foundry.PostInstall-win-x64.zip", digest, browser_download_url = url } }
    }));
}

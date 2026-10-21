// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Packages;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Tests.Packages;

/// <summary>Pins the stored post-installation package bytes, hashes and media JSON so refactors cannot change published content.</summary>
public sealed class ContentPackageCompatibilityTests
{
    private const string ExpectedManifestJson = "{\"schemaVersion\":1,\"directories\":[\"bin\",\"bin/x64\"],\"files\":[{\"relativePath\":\"bin/x64/setup.exe\",\"length\":3,\"sha256\":\"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\"},{\"relativePath\":\"readme.txt\",\"length\":0,\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}]}";
    private const string ExpectedContentHash = "476749567b79dcbda96648fa962469086feeca577da9f785d2a7f0ea4178170c";
    private const string ExpectedMediaManifestJson = "{\"schemaVersion\":1,\"id\":\"0123456789abcdef0123456789abcdef\",\"packages\":[{\"contentHash\":\"476749567b79dcbda96648fa962469086feeca577da9f785d2a7f0ea4178170c\",\"relativePath\":\"Cache/PreOobe/Packages/476749567b79dcbda96648fa962469086feeca577da9f785d2a7f0ea4178170c/files\",\"manifest\":{\"schemaVersion\":1,\"directories\":[\"bin\",\"bin/x64\"],\"files\":[{\"relativePath\":\"bin/x64/setup.exe\",\"length\":3,\"sha256\":\"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\"},{\"relativePath\":\"readme.txt\",\"length\":0,\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}]}}]}";

    [Fact]
    public void StoredPostInstallationManifestKeepsItsBytesAndHash()
    {
        ContentPackageManifest manifest = CreateManifest();

        Assert.Equal(ExpectedManifestJson, Encoding.UTF8.GetString(ContentPackageManifestCodec.Serialize(manifest)));
        Assert.Equal(ExpectedContentHash, ContentPackageManifestCodec.GetContentHash(manifest));
    }

    [Fact]
    public void StoredMediaManifestKeepsItsJson()
    {
        ContentPackageManifest manifest = CreateManifest();
        string hash = ContentPackageManifestCodec.GetContentHash(manifest);
        var media = new ContentPackageMediaManifest
        {
            Id = "0123456789abcdef0123456789abcdef",
            Packages = [new ContentPackageMediaPackage { ContentHash = hash, RelativePath = $"Cache/PreOobe/Packages/{hash}/files", Manifest = manifest }]
        };
        var options = new JsonSerializerOptions(ConfigurationJsonDefaults.SerializerOptions) { WriteIndented = false };

        Assert.Equal(ExpectedMediaManifestJson, JsonSerializer.Serialize(media, options));
    }

    private static ContentPackageManifest CreateManifest() => new()
    {
        Directories = ["bin", "bin/x64"],
        Files =
        [
            new ContentPackageFile { RelativePath = "bin/x64/setup.exe", Length = 3, Sha256 = Convert.ToHexStringLower(SHA256.HashData("abc"u8)) },
            new ContentPackageFile { RelativePath = "readme.txt", Length = 0, Sha256 = Convert.ToHexStringLower(SHA256.HashData([])) }
        ]
    };
}

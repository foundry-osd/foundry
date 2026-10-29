// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class MediaManifestIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{\"id\":\"\"}")]
    public void From_Returns32LowercaseHexCharacters(string content)
    {
        string id = MediaManifestId.From(Encoding.UTF8.GetBytes(content));

        Assert.Equal(32, id.Length);
        Assert.All(id, character => Assert.True(character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    [Fact]
    public void From_UsesTheFirstHalfOfTheSha256Digest()
    {
        byte[] content = Encoding.UTF8.GetBytes("manifest");

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content))[..32], MediaManifestId.From(content));
        Assert.NotEqual(MediaManifestId.From(content), MediaManifestId.From(Encoding.UTF8.GetBytes("manifest ")));
    }
}

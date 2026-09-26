// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Security.Cryptography;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

internal static class NativeRuntimeFixture
{
    public static PreOobePreparedContent Create(string root)
    {
        string archivePath = Path.Combine(root, "runtime.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Foundry.PostInstall.exe").Open())) writer.Write("fixture");
            using (var writer = new StreamWriter(archive.CreateEntry("Launch.cmd").Open())) writer.Write("fixture");
        }
        return new PreOobePreparedContent
        {
            RuntimeIdentifier = "win-x64",
            RuntimeArchivePath = archivePath,
            RuntimeAsset = new PreOobeRuntimeAsset
            {
                RuntimeIdentifier = "win-x64",
                AssetName = "Foundry.PostInstall-win-x64.zip",
                ArchiveLength = new FileInfo(archivePath).Length,
                ArchiveSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant(),
                ExpandedLength = 14,
                EntryCount = 2
            }
        };
    }
}

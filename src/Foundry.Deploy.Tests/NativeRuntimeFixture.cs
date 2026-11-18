// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Packages;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

/// <summary>Creates the PostInstall launch files that boot media carry in <c>X:\Foundry\PostInstall</c>.</summary>
internal static class NativeRuntimeFixture
{
    /// <summary>Informational release of the fixture runtime; Deploy never compares it.</summary>
    public const string ReleaseTag = "v26.9.29.1";

    public static PreOobePreparedContent Create(string root) =>
        PostInstallRuntimeSource.AcquireAsync(CreateFiles(root), "win-x64", CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Writes the executable, <c>Launch.cmd</c> and their manifest, and returns the runtime directory.</summary>
    public static string CreateFiles(string root)
    {
        string directory = Path.Combine(root, "postinstall-runtime");
        Directory.CreateDirectory(directory);
        var files = new List<ContentPackageFile>();
        foreach (string name in new[] { "Foundry.PostInstall.exe", "Launch.cmd" })
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("fixture");
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
            files.Add(new() { RelativePath = name, Length = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        WriteManifest(directory, new() { RuntimeIdentifier = "win-x64", ReleaseTag = ReleaseTag, Files = files });
        return directory;
    }

    public static void WriteManifest(string directory, PostInstallRuntimeManifest manifest) =>
        File.WriteAllText(Path.Combine(directory, PostInstallRuntimeManifest.FileName),
            JsonSerializer.Serialize(manifest, ConfigurationJsonDefaults.SerializerOptions));
}

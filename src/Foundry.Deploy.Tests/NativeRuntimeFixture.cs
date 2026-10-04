// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

internal static class NativeRuntimeFixture
{
    public static PreOobePreparedContent Create(string root, int contract = 1) =>
        PostInstallRuntimeSource.AcquireAsync(CreateFiles(root, contract), "win-x64", CancellationToken.None).GetAwaiter().GetResult();

    public static string CreateFiles(string root, int contract = 1)
    {
        string directory = Path.Combine(root, "postinstall-runtime");
        Directory.CreateDirectory(directory);
        var files = new List<PreOobePackageFile>();
        foreach (string name in new[] { "Foundry.PostInstall.exe", "Launch.cmd" })
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("fixture");
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
            files.Add(new() { RelativePath = name, Length = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        WriteManifest(directory, new() { ContractVersion = contract, RuntimeIdentifier = "win-x64", Files = files });
        return Path.Combine(directory, "Foundry.PostInstall.exe");
    }

    public static void WriteManifest(string directory, PostInstallRuntimeManifest manifest) =>
        File.WriteAllText(Path.Combine(directory, PostInstallRuntimeManifest.FileName),
            JsonSerializer.Serialize(manifest, ConfigurationJsonDefaults.SerializerOptions));
}

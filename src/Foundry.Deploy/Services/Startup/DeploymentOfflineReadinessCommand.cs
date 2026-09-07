// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Download;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.Configuration;
using Foundry.Utilities.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Services.Startup;

/// <summary>Runs local evidence assessment before application, telemetry or native discovery startup.</summary>
public static class DeploymentOfflineReadinessCommand
{
    public static bool IsRequested(string[] args) => args.Contains("--check-offline-readiness", StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string configuration = RequiredArgument(args, "--config");
            string output = RequiredArgument(args, "--result");
            Guid nonce = Guid.Parse(RequiredArgument(args, "--nonce"));
            if (nonce == Guid.Empty || !Path.IsPathFullyQualified(configuration) || !Path.IsPathFullyQualified(output)) return 1;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            DeploymentOfflineConfigurationSnapshot snapshot = DeploymentOfflineConfigurationSnapshot.Parse(
                await DeploymentOfflineWorkflow.ReadConfigurationAsync(configuration, deadline.Token).ConfigureAwait(false));
            WinPeMediaManifest manifest = await WinPeMediaManifestStore.ReadAsync(Path.Combine(
                DeploymentOfflineWorkflow.TrustedRoot, WinPeMediaManifestStore.RelativePath), deadline.Token).ConfigureAwait(false);
            WinPeMediaManifestStore.Validate(manifest, DeploymentOfflineWorkflow.RuntimeIdentifier);
            await WinPeMediaManifestStore.ValidateRuntimeAsync(manifest, "Foundry.Deploy", AppContext.BaseDirectory,
                deadline.Token).ConfigureAwait(false);
            string cacheRoot = Environment.GetEnvironmentVariable("FOUNDRY_VERIFIED_CACHE_ROOT") ?? @"X:\Foundry";
            var workflow = new DeploymentOfflineWorkflow(new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, new(true)));
            DeploymentOfflineReadinessResult result = await workflow.EvaluateSnapshotAsync(snapshot, null, null, cacheRoot,
                ["Driver and firmware choices must be resolved in the deployment wizard before deployment readiness can be established."],
                deadline.Token, configuration).ConfigureAwait(false);
            // Browsing proves local runtime/catalog availability, never permission to erase a target.
            bool canBrowse = true;
            string json = JsonSerializer.Serialize(new { Version = 1, Nonce = nonce, RuntimeIdentifier = DeploymentOfflineWorkflow.RuntimeIdentifier, CanBrowse = canBrowse, Result = result });
            if (Encoding.UTF8.GetByteCount(json) > 65536) return 1;
            AtomicFile.WriteAllText(output, json);
            return result.CanContinue ? 0 : 2;
        }
        catch (Exception error) when (error is UnsupportedConfigurationVersionException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or OperationCanceledException or global::System.Security.Cryptography.CryptographicException)
        {
            return 1;
        }
    }

    private static string RequiredArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || args.Count(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new ArgumentException("The offline readiness command arguments are invalid.");
        return args[index + 1];
    }
}

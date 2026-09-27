// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Publishes verified local inputs and initial state before enabling the installed-Windows launch hook.</summary>
public sealed class PreOobeTargetStagingService
{
    private readonly Action<string> _protectDirectory;
    internal Action? BeforeHookPublication { get; init; }
    public PreOobeTargetStagingService() : this(ProtectDirectory) { }
    internal PreOobeTargetStagingService(Action<string> protectDirectory) => _protectDirectory = protectDirectory;

    internal async Task StageAsync(DeploymentStepExecutionContext context, PreOobeDriverPackScriptSettings? driver, CancellationToken cancellationToken)
    {
        var content = context.PostInstallContent ?? throw new InvalidDataException("Post-installation preflight content is unavailable.");
        string partition = context.RuntimeState.TargetWindowsPartitionRoot ?? throw new InvalidDataException("Target Windows is unavailable.");
        var layout = DeploymentStorageLayout.FromPartitionRoot(partition);
        string operationId = context.RuntimeState.OperationId;
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new InvalidDataException("Post-installation operation identity is invalid.");
        string work = Path.Combine(layout.Root, "Work", "PreOobe", operationId);
        string packageRoot = Path.Combine(layout.Root, "Payloads", "PostInstall", operationId);
        var unpublishedNetworkFiles = new List<string>();
        var unpublishedStateFiles = new List<string>();
        bool hookPublished = false;
        try
        {
            foreach (string path in new[] { layout.RuntimePreOobe, layout.StatePreOobe, layout.LogsPreOobe, work, packageRoot, Path.Combine(layout.RuntimePreOobe, "Bundle") }) _protectDirectory(path);
            string planPath = Path.Combine(layout.StatePreOobe, "plan.json");
            string journalPath = Path.Combine(layout.StatePreOobe, "execution-result.json");
            if (File.Exists(planPath) || File.Exists(journalPath)) throw new InvalidDataException("An existing post-installation operation cannot be overwritten.");
            await PreOobeContentResolver.RevalidateAsync(context, content, cancellationToken).ConfigureAwait(false);
            await PreOobeRuntimeResolver.VerifyAsync(content.RuntimeArchivePath, content.RuntimeAsset, cancellationToken).ConfigureAwait(false);
            using (var archive = ZipFile.OpenRead(content.RuntimeArchivePath))
            {
                PreOobeRuntimeResolver.ValidateEntries(archive, content.RuntimeAsset);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string destination = Path.Combine(layout.RuntimePreOobe, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.Name.Length == 0) { _protectDirectory(destination); continue; }
                    _protectDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await PreOobeRuntimeResolver.CopyBoundedAsync(source, target, entry.Length, cancellationToken).ConfigureAwait(false);
                }
            }
            var actions = new List<PreOobeExecutionAction>();
            var owned = new List<PreOobeOwnedPayload>();
            var staged = new List<PreOobeStagedPackage>();
            if (driver is not null)
            {
                string relative = "Payloads/Drivers/" + Path.GetFileName(context.RuntimeState.DeferredDriverPackagePath!);
                AddBuiltIn("driver-pack", PreOobeBuiltInKind.Driver, new { commandKind = driver.CommandKind.ToString(), packagePath = relative });
                owned.Add(new() { RelativePath = relative, ConsumerActionIds = ["driver-pack"] });
            }
            if (context.NetworkProfileRoamingPayload?.DataFiles.Count > 0)
            {
                _protectDirectory(layout.PayloadsNetworkProfiles);
                foreach (var file in context.NetworkProfileRoamingPayload.DataFiles)
                {
                    string relative = "Payloads/" + file.FileName.Replace('\\', '/');
                    PreOobeRuntimeResolver.ValidateRelativePath(relative);
                    string destination = Path.Combine(layout.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                    _protectDirectory(Path.GetDirectoryName(destination)!);
                    byte[] bytes = file.Bytes ?? Encoding.UTF8.GetBytes(file.Content);
                    try
                    {
                        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        unpublishedNetworkFiles.Add(destination);
                        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                    }
                    finally { if (file.IsSensitive) CryptographicOperations.ZeroMemory(bytes); }
                    owned.Add(new() { RelativePath = relative, IsSensitive = file.IsSensitive, ConsumerActionIds = ["network-profile-roaming"] });
                }
                AddBuiltIn("network-profile-roaming", PreOobeBuiltInKind.Network, new { settingsPath = "Payloads/NetworkProfiles/import-settings.json" });
                owned.Add(new() { RelativePath = "Payloads/NetworkProfiles", IsDirectory = true, IsSensitive = true, ConsumerActionIds = ["network-profile-roaming"] });
            }
            var ai = context.Request.AiComponentRemoval;
            string[] aiNames = ai.IsEnabled ? new[] { ai.RemoveCopilot ? "Microsoft.Copilot" : null, ai.RemoveAiHub ? "Microsoft.Windows.AIHub" : null }.OfType<string>().ToArray() : [];
            if (aiNames.Length > 0) AddBuiltIn("remove-ai-components", PreOobeBuiltInKind.AiRemoval, new { packageNames = aiNames });
            if (context.Request.AppxRemoval.IsEnabled && context.Request.AppxRemoval.PackageNames.Count > 0)
                AddBuiltIn("remove-appx", PreOobeBuiltInKind.Appx, new { packageNames = context.Request.AppxRemoval.PackageNames });
            if (Steps.StagePreOobeCustomizationStep.ShouldActivateWindowsOem(context.Request)) AddBuiltIn("windows-oem-activation", PreOobeBuiltInKind.Activation, new { });
            foreach (var action in PreOobeContentResolver.ApplicableActions(context.Request)) actions.Add(new() { Id = action.Id, Name = action.Name, CustomAction = action });
            foreach (var package in content.Packages)
            {
                string relative = $"Payloads/PostInstall/{operationId}/{package.ContentHash}";
                string destinationRoot = Path.Combine(layout.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                _protectDirectory(destinationRoot);
                foreach (string directory in package.Manifest.Directories) _protectDirectory(PreOobePackagePathPolicy.Resolve(destinationRoot, directory));
                foreach (var file in package.Manifest.Files)
                {
                    string sourcePath = PreOobePackagePathPolicy.Resolve(package.SourceRoot, file.RelativePath);
                    string destination = PreOobePackagePathPolicy.Resolve(destinationRoot, file.RelativePath);
                    _protectDirectory(Path.GetDirectoryName(destination)!);
                    await using (var source = await PreOobeContentResolver.OpenVerifiedAsync(sourcePath, file.Length, file.Sha256, cancellationToken).ConfigureAwait(false))
                    await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    await using var verified = await PreOobeContentResolver.OpenVerifiedAsync(destination, file.Length, file.Sha256, cancellationToken).ConfigureAwait(false);
                }
                staged.Add(new() { ContentHash = package.ContentHash, RelativePath = relative, Manifest = package.Manifest });
                owned.Add(new()
                {
                    RelativePath = relative,
                    IsDirectory = true,
                    ConsumerActionIds = actions.Where(action => action.CustomAction?.Package?.ContentHash == package.ContentHash).Select(action => action.Id).ToArray()
                });
            }
            AddBuiltIn("cleanup", PreOobeBuiltInKind.Cleanup, new { });
            owned.Add(new() { RelativePath = $"Work/PreOobe/{operationId}", IsDirectory = true, ConsumerActionIds = actions.Where(action => action.CustomAction is not null).Select(action => action.Id).ToArray() });
            var plan = new PreOobeExecutionPlan
            {
                OperationId = operationId,
                AttemptId = Guid.NewGuid().ToString("N"),
                DiagnosticSessionId = Foundry.Utilities.Diagnostics.DiagnosticSessionContext.CurrentSessionId,
                UiCulture = global::System.Globalization.CultureInfo.CurrentUICulture.Name,
                Actions = actions,
                Packages = staged,
                OwnedPayloads = owned
            };
            byte[] planBytes = JsonSerializer.SerializeToUtf8Bytes(plan, ConfigurationJsonDefaults.SerializerOptions);
            if (planBytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("The execution plan exceeds the runtime size limit.");
            string hash = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
            var journal = new
            {
                schemaVersion = 1,
                operationId,
                attemptId = plan.AttemptId,
                planHash = hash,
                generation = 0,
                cursor = 0,
                substep = 0,
                status = "Staging",
                bootIdentity = (string?)null,
                restartCount = 0,
                deferredRestart = false,
                actions = new Dictionary<string, object>(),
                payloadDispositions = new Dictionary<string, object>(),
                unsafePayloadBootIdentity = (string?)null
            };
            DeploymentFilePublication.WriteAllText(journalPath, JsonSerializer.Serialize(journal, ConfigurationJsonDefaults.SerializerOptions), new UTF8Encoding(false));
            unpublishedStateFiles.Add(journalPath);
            DeploymentFilePublication.WriteAllText(planPath, Encoding.UTF8.GetString(planBytes), new UTF8Encoding(false));
            unpublishedStateFiles.Add(planPath);
            cancellationToken.ThrowIfCancellationRequested();
            string setupComplete = Path.Combine(partition, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
            new SetupCompleteScriptService().RemoveBlock(setupComplete, "FOUNDRY PRE-OOBE");
            new SetupCompleteScriptService().RemoveBlock(setupComplete, "FOUNDRY DRIVERPACK");
            BeforeHookPublication?.Invoke();
            new PreOobeUnattendHookService(_protectDirectory).Publish(partition, context.Request.OperatingSystem.Architecture);
            // An imported answer file may already contain the launch hook; arm its journal only after every publication succeeds.
            DeploymentFilePublication.WriteAllText(journalPath, JsonSerializer.Serialize(journal with { status = "Pending" }, ConfigurationJsonDefaults.SerializerOptions), new UTF8Encoding(false));
            hookPublished = true;
            context.RuntimeState.PreOobeRunnerPath = Path.Combine(layout.RuntimePreOobe, "Foundry.PostInstall.exe");
            context.RuntimeState.PreOobeManifestPath = planPath;

            void AddBuiltIn(string id, PreOobeBuiltInKind kind, object parameters) => actions.Add(new()
            { Id = id, Name = id, BuiltInKind = kind, Parameters = JsonSerializer.SerializeToElement(parameters, ConfigurationJsonDefaults.SerializerOptions) });
        }
        finally
        {
            if (!hookPublished)
                foreach (string path in unpublishedStateFiles.AsEnumerable().Reverse().Concat(unpublishedNetworkFiles))
                {
                    try { PreOobePackagePathPolicy.ValidateNoReparsePoints(path); File.Delete(path); }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { }
                }
        }
    }

    /// <summary>Protects files created beneath an owned target directory before sensitive inputs arrive.</summary>
    internal static void ProtectDirectory(string path)
    {
        PreOobePackagePathPolicy.ValidateNoReparsePoints(path);
        var directory = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}

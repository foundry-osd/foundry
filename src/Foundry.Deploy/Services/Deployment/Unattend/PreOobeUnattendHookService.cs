// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>Publishes the shared validated hook and a protected local source/derived hash audit.</summary>
public sealed class PreOobeUnattendHookService
{
    public const string Description = PreOobeUnattendIntegrationService.Description;
    public const string Command = PreOobeUnattendIntegrationService.Command;
    private readonly Action<string> protectDirectory;
    public PreOobeUnattendHookService() : this(PreOobeTargetStagingService.ProtectDirectory) { }
    internal PreOobeUnattendHookService(Action<string> protectDirectory) => this.protectDirectory = protectDirectory;

    public byte[] Prepare(ReadOnlySpan<byte> content, string architecture, bool integrate)
    {
        using var result = new PreOobeUnattendIntegrationService().Evaluate(content, architecture, integrate);
        return result.DerivedContent.ToArray();
    }

    /// <summary>Publishes only after executable, plan, journal and permissions have been staged. Source values never enter the audit.</summary>
    public void Publish(string windowsRoot, string architecture, bool integrate)
    {
        string path = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");
        if (File.Exists(path) && new FileInfo(path).Length > UnattendFileService.MaximumFileSizeBytes)
            throw new InvalidDataException("The answer file exceeds the supported size limit.");
        byte[] content = File.Exists(path) ? File.ReadAllBytes(path) : Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\" />");
        try
        {
            using var result = new PreOobeUnattendIntegrationService().Evaluate(content, architecture, integrate);
            string stateRoot = DeploymentStorageLayout.FromPartitionRoot(windowsRoot).StatePreOobe;
            protectDirectory(stateRoot);
            string auditPath = Path.Combine(stateRoot, "unattend-integration.json");
            if (File.Exists(auditPath)) throw new InvalidDataException("An existing answer-file integration audit cannot be overwritten.");
            bool auditCreated = false;
            try
            {
                string audit = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    sourceSha256 = result.SourceSha256,
                    derivedSha256 = result.DerivedSha256,
                    integrated = integrate,
                    modified = result.IsModified
                });
                DeploymentFilePublication.WriteAllText(auditPath, audit, new UTF8Encoding(false));
                auditCreated = true;
                if (integrate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    DeploymentFilePublication.WriteAllText(path, Encoding.UTF8.GetString(result.DerivedContent.Span), new UTF8Encoding(false));
                }
            }
            catch
            {
                if (auditCreated)
                {
                    try { File.Delete(auditPath); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
        }
        finally { CryptographicOperations.ZeroMemory(content); }
    }
}

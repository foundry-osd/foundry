// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.System;
using Microsoft.Win32;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>Returns allowlisted composition facts without escalating domain-only incompatibility.</summary>
public sealed record DomainCompositionResult(bool IsCompatible, DomainJoinSkipCode? SkipCode = null);

/// <summary>Rejects installed-image answer-file precedence conflicts before Foundry customizations can obscure them.</summary>
public class PreOobeUnattendPrecedenceService(IProcessRunner processRunner)
{
    /// <summary>Checks embedded domain components and name ambiguity before later customization can hide them.</summary>
    public virtual Task<DomainCompositionResult> CheckDomainCompositionAsync(string partition, string architecture, string expectedName,
        bool usesCustomUnattend, CancellationToken cancellationToken) =>
        InspectDomainCompositionAsync(partition, architecture, expectedName, usesCustomUnattend, false, cancellationToken);

    internal static async Task<DomainCompositionResult> InspectDomainCompositionAsync(string partition, string architecture,
        string expectedName, bool usesCustomUnattend, bool final, CancellationToken cancellationToken)
    {
        string path = Path.Combine(partition, "Windows", "Panther", "unattend.xml");
        if (!File.Exists(path)) return final ? new(false, DomainJoinSkipCode.ComputerNameMismatch) : new(true);
        Foundry.Core.Services.Packages.PreOobePackagePathPolicy.ValidateNoReparsePoints(path);
        if (new FileInfo(path).Length > UnattendFileService.MaximumFileSizeBytes)
            throw new InvalidDataException("The answer file exceeds the supported size limit.");
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            if (bytes.Length > UnattendFileService.MaximumFileSizeBytes)
                throw new InvalidDataException("The answer file exceeds the supported size limit.");
            using var stream = new MemoryStream(bytes, writable: false);
            var document = XDocument.Load(stream);
            XNamespace ns = "urn:schemas-microsoft-com:unattend";
            if (document.Root?.Name != ns + "unattend") throw new InvalidDataException("The answer file namespace is invalid.");
            var components = document.Descendants(ns + "component").ToArray();
            if (components.Any(component => (string?)component.Attribute("name") == "Microsoft-Windows-UnattendedJoin"))
                return new(false, DomainJoinSkipCode.EmbeddedDomainJoin);
            string target = architecture.Equals("x64", StringComparison.OrdinalIgnoreCase) ? "amd64" : architecture.ToLowerInvariant();
            var names = components.Where(component => (string?)component.Attribute("name") == "Microsoft-Windows-Shell-Setup" &&
                (string?)component.Parent?.Attribute("pass") == "specialize" &&
                ((string?)component.Attribute("processorArchitecture") is "*" or "neutral" ||
                 string.Equals((string?)component.Attribute("processorArchitecture"), target, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(component => component.Elements(ns + "ComputerName")).ToArray();
            if (names.Length > 1) return new(false, DomainJoinSkipCode.AmbiguousComputerName);
            if ((final || usesCustomUnattend && names.Length == 1) &&
                (names.Length != 1 || !names[0].Value.Equals(expectedName, StringComparison.Ordinal)))
                return new(false, DomainJoinSkipCode.ComputerNameMismatch);
            return new(true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public virtual async Task ValidateAsync(string partition, string architecture, CancellationToken cancellationToken)
    {
        string panther = Path.Combine(partition, "Windows", "Panther");
        foreach (string name in new[] { "unattend.xml", "autounattend.xml" })
            if (File.Exists(Path.Combine(panther, "Unattend", name)))
                throw new InvalidDataException("A higher-priority embedded answer file conflicts with the Foundry post-installation hook.");
        string answer = Path.Combine(panther, "unattend.xml");
        if (File.Exists(answer))
        {
            byte[] bytes = await File.ReadAllBytesAsync(answer, cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] validated = new PreOobeUnattendHookService().Prepare(bytes, architecture);
                CryptographicOperations.ZeroMemory(validated);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        string mount = "FoundryPostInstall_" + Guid.NewGuid().ToString("N");
        var registry = new OfflineRegistryWriter(processRunner);
        await registry.WithLoadedHiveAsync(@"HKLM\" + mount, Path.Combine(partition, "Windows", "System32", "config", "SYSTEM"), partition,
            (_, _) =>
            {
                using RegistryKey? setup = Registry.LocalMachine.OpenSubKey(mount + @"\Setup", writable: false);
                if (setup?.GetValue("UnattendFile", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is object pointer &&
                    !string.IsNullOrWhiteSpace(pointer.ToString()))
                    throw new InvalidDataException("An offline UnattendFile registry override conflicts with the Foundry post-installation hook.");
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
    }
}

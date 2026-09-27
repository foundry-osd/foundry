// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Deploy.Services.System;
using Microsoft.Win32;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>Rejects installed-image answer-file precedence conflicts before Foundry customizations can obscure them.</summary>
public class PreOobeUnattendPrecedenceService(IProcessRunner processRunner)
{
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

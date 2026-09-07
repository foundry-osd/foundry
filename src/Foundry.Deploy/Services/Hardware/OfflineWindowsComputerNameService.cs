// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.System;
using ComputerNameRules = Foundry.Core.Services.Configuration.ComputerNameRules;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Foundry.Deploy.Services.Deployment.Unattend;

namespace Foundry.Deploy.Services.Hardware;

/// <summary>Reads existing Windows names using one uniquely owned, reconciled offline hive.</summary>
public sealed class OfflineWindowsComputerNameService : IOfflineWindowsComputerNameService
{
    private readonly OfflineRegistryWriter _hives;
    private readonly ILogger<OfflineWindowsComputerNameService> _logger;
    private readonly Func<IEnumerable<string>> _candidateHives;
    private readonly Func<string, string?> _readComputerName;

    public OfflineWindowsComputerNameService(IProcessRunner processRunner, ILogger<OfflineWindowsComputerNameService> logger)
        : this(processRunner, logger, () => GetCandidateDriveLetters()
            .Select(drive => $@"{drive}:\Windows\System32\config\SYSTEM").Where(File.Exists), ReadComputerName)
    { }

    internal OfflineWindowsComputerNameService(IProcessRunner processRunner, ILogger<OfflineWindowsComputerNameService> logger,
        Func<IEnumerable<string>> candidateHives, Func<string, string?> readComputerName)
    {
        _hives = new(processRunner);
        _logger = logger;
        _candidateHives = candidateHives;
        _readComputerName = readComputerName;
    }

    public async Task<string?> TryGetOfflineComputerNameAsync(CancellationToken cancellationToken = default)
    {
        foreach (string hivePath in _candidateHives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? name = null;
            try
            {
                await _hives.WithLoadedHiveAsync(@"HKLM\FoundryOfflineSystem", hivePath, Path.GetTempPath(), (hive, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    name = _readComputerName(hive.MountName);
                    return Task.CompletedTask;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException && !error.Data.Contains("FoundryRecoveryDiagnostic"))
            {
                _logger.LogWarning("Unable to read an existing Windows computer name. ErrorType={ErrorType}", error.GetType().Name);
                continue;
            }
            if (string.IsNullOrWhiteSpace(name)) continue;
            string normalized = ComputerNameRules.Normalize(name);
            if (ComputerNameRules.IsValid(normalized)) return normalized;
        }
        return null;
    }

    private static string? ReadComputerName(string mountName)
    {
        string subkey = mountName[(mountName.IndexOf('\\') + 1)..];
        string controlSet = "ControlSet001";
        using (RegistryKey? select = Registry.LocalMachine.OpenSubKey($@"{subkey}\Select"))
        {
            if (select?.GetValue("Current") is int current && current > 0) controlSet = $"ControlSet{current:D3}";
        }
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{subkey}\{controlSet}\Control\ComputerName\ComputerName");
        return key?.GetValue("ComputerName") as string;
    }

    /// <summary>
    /// Returns candidate drive letters to scan, excluding the current system drive (WinPE's X:)
    /// and the legacy floppy drives (A:, B:).
    /// </summary>
    private static IEnumerable<char> GetCandidateDriveLetters()
    {
        string systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "X:";
        char systemLetter = char.ToUpperInvariant(systemDrive.Length > 0 ? systemDrive[0] : 'X');

        return Enumerable
            .Range('C', 'Z' - 'C' + 1)
            .Select(c => (char)c)
            .Where(c => c != systemLetter);
    }
}

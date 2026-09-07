// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.System;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Provides shared primitives for loading offline Windows registry hives and writing values with reg.exe.
/// </summary>
internal sealed class OfflineRegistryWriter
{
    private readonly IProcessRunner _processRunner;
    private RecoveryResourceDiagnostic? _recovery;

    /// <summary>
    /// Initializes an offline registry writer backed by the deployment process runner.
    /// </summary>
    /// <param name="processRunner">The process runner used for reg.exe operations.</param>
    public OfflineRegistryWriter(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <summary>
    /// Loads a hive, runs the supplied action, and unloads the hive even when the action fails.
    /// </summary>
    /// <param name="mountName">Temporary registry mount name, such as <c>HKLM\FoundrySoftware</c>.</param>
    /// <param name="hivePath">Path to the offline hive file.</param>
    /// <param name="workingDirectory">Directory used for command execution.</param>
    /// <param name="action">Action that writes values under the loaded hive.</param>
    /// <param name="cancellationToken">Token that cancels hive loading and write operations.</param>
    /// <returns>A task that completes after the hive is unloaded.</returns>
    public async Task WithLoadedHiveAsync(
        string mountName,
        string hivePath,
        string workingDirectory,
        Func<OfflineRegistryHive, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_recovery is not null)
        {
            var blocked = new InvalidOperationException("An offline registry hive requires recovery before further writes.");
            blocked.Data["FoundryRecoveryDiagnostic"] = _recovery;
            throw blocked;
        }
        if (!Regex.IsMatch(mountName, @"^HK(?:LM|U)\\Foundry[A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("A Foundry-owned registry mount prefix is required.", nameof(mountName));
        string ownedMount = mountName + "_" + Guid.NewGuid().ToString("N");
        if (await IsMountedAsync(ownedMount, workingDirectory, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The unique offline hive mount is already present.");
        Exception? primary = null;
        bool attemptedLoad = false;
        bool nativeUncertain = false;
        try
        {
            attemptedLoad = true;
            await RunRequiredAsync("reg.exe", ["LOAD", ownedMount, hivePath], workingDirectory, cancellationToken).ConfigureAwait(false);
            var hive = new OfflineRegistryHive(this, ownedMount, workingDirectory);
            await action(hive, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            nativeUncertain = HasUncertainNativeProcess(error);
        }
        finally
        {
            if (attemptedLoad)
            {
                try
                {
                    if (nativeUncertain) throw new InvalidOperationException("The native hive operation has not confirmed process exit.");
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    if (await IsMountedAsync(ownedMount, workingDirectory, cleanup.Token).ConfigureAwait(false))
                    {
                        await RunRequiredAsync("reg.exe", ["UNLOAD", ownedMount], workingDirectory, cleanup.Token).ConfigureAwait(false);
                        if (await IsMountedAsync(ownedMount, workingDirectory, cleanup.Token).ConfigureAwait(false))
                            throw new InvalidOperationException("The offline registry hive remains mounted after unload.");
                    }
                }
                catch (Exception cleanupError)
                {
                    _recovery = new(RecoveryResourceState.RecoveryRequired, "RegistryHive", ownedMount, hivePath,
                        nativeUncertain ? "native_process_uncertain" : "hive_cleanup_unresolved");
                    if (primary is null) primary = cleanupError;
                    else primary.Data["FoundryCleanupFailure"] = cleanupError;
                    primary.Data["FoundryRecoveryDiagnostic"] = _recovery;
                }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static bool HasUncertainNativeProcess(Exception error) =>
        error.Data["ProcessRootExitConfirmed"] is false || error.Data["ProcessTreeTerminationConfirmed"] is false ||
        (error is TimeoutException && error.Data["ProcessRootExitConfirmed"] is not true);

    private async Task<bool> IsMountedAsync(string mountName, string workingDirectory, CancellationToken token)
    {
        string hive = mountName.StartsWith(@"HKLM\", StringComparison.Ordinal) ? "LocalMachine" : "Users";
        string subkey = mountName[(mountName.IndexOf('\\') + 1)..];
        string script = "$ErrorActionPreference='Stop'; $key=$null; try { $key=[Microsoft.Win32.Registry]::" + hive +
            ".OpenSubKey('" + subkey + "'); if ($null -eq $key) { 'FOUNDRY_HIVE_ABSENT' } else { 'FOUNDRY_HIVE_PRESENT' } } finally { if ($null -ne $key) { $key.Dispose() } }";
        ProcessExecutionResult result = await _processRunner.RunAsync("powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            workingDirectory, token, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        if (!result.IsSuccess) throw new InvalidOperationException("Offline hive state could not be confirmed.");
        return result.StandardOutput.Trim() switch
        {
            "FOUNDRY_HIVE_PRESENT" => true,
            "FOUNDRY_HIVE_ABSENT" => false,
            _ => throw new InvalidOperationException("Offline hive discovery returned an unknown state.")
        };
    }

    private async Task<string> ReadCurrentControlSetAsync(string mountName, string workingDirectory, CancellationToken token)
    {
        string subkey = mountName[(mountName.IndexOf('\\') + 1)..];
        string script = "$ErrorActionPreference='Stop'; $key=$null; try { $key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('" +
            subkey + "\\Select'); $number=1; if ($null -ne $key) { $value=$key.GetValue('Current'); if ($value -is [int] -and $value -gt 0) { $number=$value } }; 'FOUNDRY_CONTROL_SET:'+ $number } finally { if ($null -ne $key) { $key.Dispose() } }";
        ProcessExecutionResult result = await _processRunner.RunAsync("powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            workingDirectory, token, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        string value = result.StandardOutput.Trim();
        const string prefix = "FOUNDRY_CONTROL_SET:";
        if (!result.IsSuccess || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(value[prefix.Length..], out int current) || current < 1 || current > 999)
            throw new InvalidOperationException("The offline Windows control set could not be read.");
        return $"ControlSet{current:D3}";
    }

    private Task AddDwordAsync(
        string keyPath,
        string valueName,
        int value,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RunRequiredAsync(
            "reg.exe",
            ["ADD", keyPath, "/v", valueName, "/t", "REG_DWORD", "/d", value.ToString(), "/f"],
            workingDirectory,
            cancellationToken);
    }

    private async Task RunRequiredAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken, TimeSpan.FromMinutes(2))
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new DeploymentProcessException(
                $"{fileName} failed.{Environment.NewLine}{result.ToDiagnosticText()}",
                result.ExitCode);
        }
    }

    /// <summary>
    /// Represents a loaded offline registry hive and exposes value writes relative to its mount point.
    /// </summary>
    internal sealed class OfflineRegistryHive
    {
        private readonly OfflineRegistryWriter _writer;
        private readonly string _workingDirectory;

        /// <summary>
        /// Initializes a loaded hive context.
        /// </summary>
        /// <param name="writer">The writer that owns reg.exe execution.</param>
        /// <param name="mountName">Temporary registry mount name.</param>
        /// <param name="workingDirectory">Directory used for command execution.</param>
        public OfflineRegistryHive(OfflineRegistryWriter writer, string mountName, string workingDirectory)
        {
            _writer = writer;
            MountName = mountName;
            _workingDirectory = workingDirectory;
        }

        /// <summary>
        /// Gets the temporary registry mount name used for this hive.
        /// </summary>
        public string MountName { get; }

        /// <summary>Reads the selected Windows control set from this uniquely owned hive.</summary>
        public Task<string> ReadCurrentControlSetAsync(CancellationToken cancellationToken)
            => _writer.ReadCurrentControlSetAsync(MountName, _workingDirectory, cancellationToken);

        /// <summary>Writes a REG_DWORD value below this loaded hive.</summary>
        /// <param name="relativeKeyPath">Registry key path relative to the hive mount point.</param>
        /// <param name="valueName">Registry value name.</param>
        /// <param name="value">DWORD value to write.</param>
        /// <param name="cancellationToken">Token that cancels the write operation.</param>
        /// <returns>A task that completes after reg.exe writes the value.</returns>
        public Task AddDwordAsync(
            string relativeKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken)
        {
            string trimmedPath = relativeKeyPath.TrimStart('\\');
            string keyPath = string.IsNullOrWhiteSpace(trimmedPath)
                ? MountName
                : $@"{MountName}\{trimmedPath}";

            return _writer.AddDwordAsync(keyPath, valueName, value, _workingDirectory, cancellationToken);
        }
    }
}

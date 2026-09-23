// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Provisions the pre-OOBE PowerShell runner inside an offline Windows installation.
/// </summary>
public sealed class PreOobeScriptProvisioningService : IPreOobeScriptProvisioningService
{
    private const string SetupCompleteMarkerKey = "FOUNDRY PRE-OOBE";
    private const string RunnerFileName = "Invoke-FoundryPreOobe.ps1";
    private const string ManifestFileName = "pre-oobe-manifest.json";
    private static readonly string RuntimePreOobeRoot = DeploymentStorageLayout.RuntimePath(@"Runtime\PreOobe");
    private const string RuntimePreOobeLogRoot = "%SystemRoot%\\Temp\\Foundry\\Logs\\PreOobe";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly ISetupCompleteScriptService _setupCompleteScriptService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreOobeScriptProvisioningService"/> class.
    /// </summary>
    /// <param name="setupCompleteScriptService">Service used to update SetupComplete.cmd idempotently.</param>
    public PreOobeScriptProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
    {
        _setupCompleteScriptService = setupCompleteScriptService;
    }

    /// <inheritdoc />
    public PreOobeScriptProvisioningResult Provision(
        string targetWindowsPartitionRoot,
        IEnumerable<PreOobeScriptDefinition> scripts,
        string? operationId = null)
    {
        if (string.IsNullOrWhiteSpace(targetWindowsPartitionRoot))
        {
            throw new ArgumentException("Target Windows partition root is required.", nameof(targetWindowsPartitionRoot));
        }

        ArgumentNullException.ThrowIfNull(scripts);

        PreOobeScriptDefinition[] orderedScripts = scripts
            .Select(NormalizeScript)
            .Where(script => !string.IsNullOrWhiteSpace(script.Id))
            .GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(script => script.Priority)
            .ThenBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedScripts.Length == 0)
        {
            throw new InvalidOperationException("At least one pre-OOBE script is required.");
        }

        operationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId;
        var layout = DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot);
        string preOobeRoot = layout.RuntimePreOobe;
        string scriptsRoot = GetScriptsRoot(targetWindowsPartitionRoot);
        string dataRoot = GetDataRoot(targetWindowsPartitionRoot);
        string runnerPath = Path.Combine(preOobeRoot, RunnerFileName);
        string manifestPath = Path.Combine(layout.StatePreOobe, ManifestFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(preOobeRoot);
        Directory.CreateDirectory(scriptsRoot);
        Directory.CreateDirectory(layout.StatePreOobe);

        string manifest = BuildManifest(orderedScripts, operationId);
        string[] stagedScriptPaths = StageScripts(scriptsRoot, orderedScripts);
        StageDataFiles(dataRoot, orderedScripts);
        DeploymentFilePublication.WriteAllText(runnerPath, BuildRunner(orderedScripts), Utf8NoBom);
        DeploymentFilePublication.WriteAllText(manifestPath, manifest, Utf8NoBom);

        _setupCompleteScriptService.RemoveBlock(setupCompletePath, "FOUNDRY DRIVERPACK");
        _setupCompleteScriptService.EnsureBlock(
            setupCompletePath,
            SetupCompleteMarkerKey,
            BuildSetupCompleteLauncher());

        return new PreOobeScriptProvisioningResult
        {
            SetupCompletePath = setupCompletePath,
            RunnerPath = runnerPath,
            ManifestPath = manifestPath,
            StagedScriptPaths = stagedScriptPaths
        };
    }

    private static string GetPreOobeRoot(string targetWindowsPartitionRoot)
    {
        return DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot).RuntimePreOobe;
    }

    private static string GetScriptsRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(GetPreOobeRoot(targetWindowsPartitionRoot), "Scripts");
    }

    private static string GetDataRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(DeploymentStorageLayout.FromPartitionRoot(targetWindowsPartitionRoot).Root, "Payloads");
    }

    private static string GetSetupCompletePath(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
    }

    private static PreOobeScriptDefinition NormalizeScript(PreOobeScriptDefinition script)
    {
        string fileName = script.FileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Pre-OOBE script file name is required.", nameof(script));
        }

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Pre-OOBE script file name '{script.FileName}' must not contain a path.", nameof(script));
        }

        string resourceName = script.ResourceName.Trim();
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Pre-OOBE script resource name is required.", nameof(script));
        }

        if (script.TimeoutSeconds is <= 0 or > int.MaxValue / 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(script), "Pre-OOBE script timeout must be a positive number of seconds that fits in milliseconds.");
        }

        return script with
        {
            Id = script.Id.Trim(),
            FileName = fileName,
            ResourceName = resourceName,
            Arguments = script.Arguments
                .Where(argument => argument is not null)
                .Select(argument => argument.Trim())
                .ToArray(),
            DataFiles = script.DataFiles
                .Where(dataFile => dataFile is not null)
                .Select(NormalizeDataFile)
                .ToArray()
        };
    }

    private static PreOobeScriptDataFile NormalizeDataFile(PreOobeScriptDataFile dataFile)
    {
        string fileName = NormalizeRelativeDataPath(dataFile.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Pre-OOBE data file name is required.", nameof(dataFile));
        }

        return dataFile with
        {
            FileName = fileName,
            Content = dataFile.Content,
            Bytes = dataFile.Bytes
        };
    }

    private static string[] StageScripts(string scriptsRoot, IReadOnlyList<PreOobeScriptDefinition> orderedScripts)
    {
        Assembly assembly = typeof(PreOobeScriptProvisioningService).Assembly;
        var stagedPaths = new List<string>(orderedScripts.Count);

        foreach (PreOobeScriptDefinition script in orderedScripts)
        {
            using Stream? stream = assembly.GetManifestResourceStream(script.ResourceName);
            if (stream is null)
            {
                throw new InvalidOperationException($"Embedded pre-OOBE script resource '{script.ResourceName}' was not found.");
            }

            string destinationPath = Path.Combine(scriptsRoot, script.FileName);
            using FileStream destination = File.Create(destinationPath);
            stream.CopyTo(destination);
            stagedPaths.Add(destinationPath);
        }

        return stagedPaths.ToArray();
    }

    private static void StageDataFiles(string dataRoot, IReadOnlyList<PreOobeScriptDefinition> orderedScripts)
    {
        foreach (PreOobeScriptDataFile dataFile in orderedScripts.SelectMany(script => script.DataFiles))
        {
            string destinationPath = Path.Combine(dataRoot, GetPayloadRelativePath(dataFile.FileName));
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                CreateRestrictedDirectory(destinationDirectory);
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }

            if (dataFile.Bytes is not null)
            {
                File.WriteAllBytes(destinationPath, dataFile.Bytes);
            }
            else
            {
                File.WriteAllText(destinationPath, dataFile.Content, Utf8NoBom);
            }

            if (dataFile.IsSensitive)
            {
                TryMarkSensitiveFile(destinationPath);
            }
        }
    }

    private static string BuildRunner(IReadOnlyList<PreOobeScriptDefinition> orderedScripts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine("$preOobeRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Runtime\\PreOobe'");
        builder.AppendLine("$scriptsRoot = Join-Path $preOobeRoot 'Scripts'");
        builder.AppendLine();
        AppendSupervisedScriptFunction(builder);
        builder.AppendLine("function Invoke-FoundryProcess {");
        builder.AppendLine("    param(");
        builder.AppendLine("        [Parameter(Mandatory = $true)]");
        builder.AppendLine("        [string]$ScriptPath,");
        builder.AppendLine("        [string[]]$Arguments = @(),");
        builder.AppendLine("        [int]$TimeoutSeconds = 0,");
        builder.AppendLine("        [switch]$ContinueOnError");
        builder.AppendLine("    )");
        builder.AppendLine();
        builder.AppendLine("    $name = [System.IO.Path]::GetFileNameWithoutExtension($ScriptPath)");
        builder.AppendLine("    $logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\PreOobe'");
        builder.AppendLine("    $transcriptPath = Join-Path $logRoot \"$name.transcript.log\"");
        builder.AppendLine("    if ($TimeoutSeconds -gt 0 -or $ContinueOnError) {");
        builder.AppendLine("        Invoke-FoundrySupervisedScript -ScriptPath $ScriptPath -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds");
        builder.AppendLine("        return");
        builder.AppendLine("    }");
        builder.AppendLine("    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments");
        builder.AppendLine("    if ($LASTEXITCODE -ne 0) {");
        builder.AppendLine("        throw \"Pre-OOBE script '$ScriptPath' failed with exit code $LASTEXITCODE. See '$transcriptPath'.\"");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        using (Stream lifecycle = typeof(PreOobeScriptProvisioningService).Assembly.GetManifestResourceStream("Foundry.Deploy.PreOobe.Runner-Lifecycle.ps1")!)
        using (var reader = new StreamReader(lifecycle))
        {
            builder.AppendLine(reader.ReadToEnd());
        }
        builder.AppendLine("try {");
        builder.AppendLine("    if (Initialize-FoundryAttempt) {");

        PreOobeScriptDefinition[] cleanupScripts = orderedScripts
            .Where(static script => script.Priority == PreOobeScriptPriority.Cleanup)
            .ToArray();
        PreOobeScriptDefinition[] mainScripts = orderedScripts
            .Where(static script => script.Priority != PreOobeScriptPriority.Cleanup)
            .ToArray();

        builder.AppendLine("try {");
        foreach (PreOobeScriptDefinition script in mainScripts)
        {
            AppendInvokeFoundryScript(builder, script, "    ");
        }
        builder.AppendLine("}");
        builder.AppendLine("finally {");
        foreach (PreOobeScriptDefinition script in cleanupScripts)
        {
            builder.AppendLine("    try {");
            AppendInvokeFoundryScript(builder, script, "        ");
            builder.AppendLine("    }");
            builder.AppendLine("    catch {");
            builder.AppendLine("        Write-Warning $_");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");

        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine("catch { $script:attemptFailed = $true; throw }");
        builder.AppendLine("finally {");
        builder.AppendLine("    try { Complete-FoundryAttempt } finally { $runnerLease.Dispose() }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendSupervisedScriptFunction(StringBuilder builder)
    {
        builder.AppendLine("""
            function Invoke-FoundrySupervisedScript {
                param([string]$ScriptPath, [string[]]$Arguments, [int]$TimeoutSeconds, [switch]$ContinueOnError)

                $name = [System.IO.Path]::GetFileNameWithoutExtension($ScriptPath)
                $process = New-Object System.Diagnostics.Process
                $started = $false
                $failure = "Pre-OOBE script '$name' could not complete."
                try {
                    $process.StartInfo.FileName = Join-Path $PSHOME 'powershell.exe'
                    $tokens = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Arguments
                    $quotedTokens = foreach ($token in $tokens) {
                        $escaped = [regex]::Replace([string]$token, '(\\*)"', '$1$1\"')
                        '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
                    }
                    $process.StartInfo.Arguments = $quotedTokens -join ' '
                    $process.StartInfo.UseShellExecute = $false
                    $process.StartInfo.CreateNoWindow = $true
                    $process.StartInfo.RedirectStandardOutput = $true
                    $process.StartInfo.RedirectStandardError = $true
                    $started = $process.Start()
                    $output = $process.StandardOutput.ReadToEndAsync()
                    $errorOutput = $process.StandardError.ReadToEndAsync()
                    $waitMilliseconds = -1
                    if ($TimeoutSeconds -gt 0) { $waitMilliseconds = $TimeoutSeconds * 1000 }
                    if (-not $process.WaitForExit($waitMilliseconds)) {
                        $failure = "Pre-OOBE script '$name' timed out after $TimeoutSeconds seconds."
                        throw $failure
                    }

                    # Descendants can retain inherited pipes after the script exits; never drain them indefinitely.
                    if ($output.Wait(1000)) { Write-Output $output.Result }
                    if ($process.ExitCode -ne 0) {
                        $failure = "Pre-OOBE script '$name' failed with exit code $($process.ExitCode)."
                        throw $failure
                    }
                }
                catch {
                    if ($ContinueOnError) { Write-Warning $failure }
                    else { throw $failure }
                }
                finally {
                    try {
                        if ($started -and -not $process.HasExited) {
                            $process.Kill()
                            [void]$process.WaitForExit(5000)
                        }
                    }
                    catch { Write-Warning "Pre-OOBE script '$name' could not be stopped." }
                    $process.Dispose()
                }
            }

            """);
    }

    private static void AppendInvokeFoundryScript(StringBuilder builder, PreOobeScriptDefinition script, string indent = "")
    {
        builder.Append(indent);
        builder.Append("Invoke-FoundryScript -ScriptId ");
        builder.Append(ToPowerShellString(script.Id));
        builder.Append(" -ScriptPath (Join-Path $scriptsRoot ");
        builder.Append(ToPowerShellString(script.FileName));
        builder.Append(") -Arguments ");
        builder.Append(ToPowerShellArray(script.Arguments));
        if (script.TimeoutSeconds is int timeoutSeconds)
        {
            builder.Append(" -TimeoutSeconds ");
            builder.Append(timeoutSeconds);
        }
        if (script.ContinueOnError)
        {
            builder.Append(" -ContinueOnError");
        }
        builder.AppendLine();
    }

    private static string BuildSetupCompleteLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                $"mkdir \"{RuntimePreOobeLogRoot}\" >nul 2>&1",
                $"echo [%date% %time%] Starting Foundry pre-OOBE runner.>\"{RuntimePreOobeLogRoot}\\SetupComplete.log\"",
                $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{RuntimePreOobeRoot}\\{RunnerFileName}\" >>\"{RuntimePreOobeLogRoot}\\SetupComplete.log\" 2>&1",
                "set \"FOUNDRY_PREOOBE_EXIT=%ERRORLEVEL%\"",
                $"echo [%date% %time%] Foundry pre-OOBE runner exited with %FOUNDRY_PREOOBE_EXIT%.>>\"{RuntimePreOobeLogRoot}\\SetupComplete.log\"",
                "if not \"%FOUNDRY_PREOOBE_EXIT%\"==\"0\" exit /b %FOUNDRY_PREOOBE_EXIT%"
            ]);
    }

    private static string BuildManifest(IReadOnlyList<PreOobeScriptDefinition> orderedScripts, string operationId)
    {
        string json = JsonSerializer.Serialize(new
        {
            operationId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            scripts = orderedScripts.Select(script => new
            {
                id = script.Id,
                fileName = script.FileName,
                priority = (int)script.Priority,
                arguments = script.Arguments,
                timeoutSeconds = script.TimeoutSeconds,
                continueOnError = script.ContinueOnError,
                dataFiles = script.DataFiles.Select(dataFile => dataFile.FileName),
                inputs = GetOwnedInputs(script)
            })
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return json + Environment.NewLine;
    }

    private static string GetPayloadRelativePath(string fileName) =>
        fileName.StartsWith("NetworkProfiles" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : Path.Combine("Customization", fileName);

    private static object[] GetOwnedInputs(PreOobeScriptDefinition script)
    {
        var inputs = script.DataFiles.Select(file => new { relativePath = GetPayloadRelativePath(file.FileName), sensitive = file.IsSensitive }).ToList();
        int packageIndex = script.Arguments.ToList().IndexOf("-PackagePath");
        if (packageIndex >= 0 && packageIndex + 1 < script.Arguments.Count)
        {
            inputs.Add(new { relativePath = Path.Combine("Drivers", Path.GetFileName(script.Arguments[packageIndex + 1])), sensitive = false });
        }
        if (inputs.Count > 256)
        {
            throw new InvalidOperationException("A pre-OOBE script cannot own more than 256 inputs.");
        }
        return inputs.Cast<object>().ToArray();
    }

    // Fail closed before decrypted bytes reach disk. All payload directories use the same restricted policy.
    private static void CreateRestrictedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var identities = new[]
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            WindowsIdentity.GetCurrent().User!
        };
        foreach (SecurityIdentifier identity in identities.Distinct())
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static string ToPowerShellArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "@()";
        }

        return "@(" + string.Join(", ", values.Select(ToPowerShellString)) + ")";
    }

    private static string ToPowerShellString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string NormalizeRelativeDataPath(string fileName)
    {
        string normalized = fileName.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
        {
            throw new ArgumentException($"Pre-OOBE data file name '{fileName}' must be relative.", nameof(fileName));
        }

        string[] segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException($"Pre-OOBE data file name '{fileName}' is invalid.", nameof(fileName));
        }

        return Path.Combine(segments);
    }

    private static void TryMarkSensitiveFile(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

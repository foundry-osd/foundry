using System.IO;
using System.Reflection;
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
    private const string RuntimePreOobeRoot = "%SystemRoot%\\Temp\\Foundry\\PreOobe";
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
        IEnumerable<PreOobeScriptDefinition> scripts)
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

        string preOobeRoot = GetPreOobeRoot(targetWindowsPartitionRoot);
        string scriptsRoot = GetScriptsRoot(targetWindowsPartitionRoot);
        string dataRoot = GetDataRoot(targetWindowsPartitionRoot);
        string runnerPath = Path.Combine(preOobeRoot, RunnerFileName);
        string manifestPath = Path.Combine(preOobeRoot, ManifestFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(preOobeRoot);
        Directory.CreateDirectory(scriptsRoot);
        Directory.CreateDirectory(dataRoot);

        string[] stagedScriptPaths = StageScripts(scriptsRoot, orderedScripts);
        StageDataFiles(dataRoot, orderedScripts);
        File.WriteAllText(runnerPath, BuildRunner(orderedScripts), Utf8NoBom);
        File.WriteAllText(manifestPath, BuildManifest(orderedScripts), Utf8NoBom);

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
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "PreOobe");
    }

    private static string GetScriptsRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(GetPreOobeRoot(targetWindowsPartitionRoot), "Scripts");
    }

    private static string GetDataRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(GetPreOobeRoot(targetWindowsPartitionRoot), "Data");
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
            string destinationPath = Path.Combine(dataRoot, dataFile.FileName);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
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
        builder.AppendLine("$preOobeRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\PreOobe'");
        builder.AppendLine("$scriptsRoot = Join-Path $preOobeRoot 'Scripts'");
        builder.AppendLine();
        builder.AppendLine("function Invoke-FoundryScript {");
        builder.AppendLine("    param(");
        builder.AppendLine("        [Parameter(Mandatory = $true)]");
        builder.AppendLine("        [string]$ScriptPath,");
        builder.AppendLine("        [string[]]$Arguments = @()");
        builder.AppendLine("    )");
        builder.AppendLine();
        builder.AppendLine("    $name = [System.IO.Path]::GetFileNameWithoutExtension($ScriptPath)");
        builder.AppendLine("    $logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\PreOobe'");
        builder.AppendLine("    $transcriptPath = Join-Path $logRoot \"$name.transcript.log\"");
        builder.AppendLine("    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments");
        builder.AppendLine("    if ($LASTEXITCODE -ne 0) {");
        builder.AppendLine("        throw \"Pre-OOBE script '$ScriptPath' failed with exit code $LASTEXITCODE. See '$transcriptPath'.\"");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        PreOobeScriptDefinition[] cleanupScripts = orderedScripts
            .Where(static script => script.Priority == PreOobeScriptPriority.Cleanup)
            .ToArray();
        PreOobeScriptDefinition[] mainScripts = orderedScripts
            .Where(static script => script.Priority != PreOobeScriptPriority.Cleanup)
            .ToArray();

        if (cleanupScripts.Length == 0)
        {
            foreach (PreOobeScriptDefinition script in mainScripts)
            {
                AppendInvokeFoundryScript(builder, script);
            }

            return builder.ToString();
        }

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

        return builder.ToString();
    }

    private static void AppendInvokeFoundryScript(StringBuilder builder, PreOobeScriptDefinition script, string indent = "")
    {
        builder.Append(indent);
        builder.Append("Invoke-FoundryScript -ScriptPath (Join-Path $scriptsRoot ");
        builder.Append(ToPowerShellString(script.FileName));
        builder.Append(") -Arguments ");
        builder.AppendLine(ToPowerShellArray(script.Arguments));
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

    private static string BuildManifest(IReadOnlyList<PreOobeScriptDefinition> orderedScripts)
    {
        string json = JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            scripts = orderedScripts.Select(script => new
            {
                id = script.Id,
                fileName = script.FileName,
                priority = (int)script.Priority,
                arguments = script.Arguments,
                dataFiles = script.DataFiles.Select(dataFile => dataFile.FileName)
            })
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return json + Environment.NewLine;
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

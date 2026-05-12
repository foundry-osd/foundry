using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

public sealed class PreOobeScriptProvisioningService : IPreOobeScriptProvisioningService
{
    private const string SetupCompleteMarkerKey = "FOUNDRY PRE-OOBE";
    private const string RunnerFileName = "Invoke-FoundryPreOobe.ps1";
    private const string ManifestFileName = "pre-oobe-manifest.json";
    private const string RuntimePreOobeRoot = "%SystemRoot%\\Temp\\Foundry\\PreOobe";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly ISetupCompleteScriptService _setupCompleteScriptService;

    public PreOobeScriptProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
    {
        _setupCompleteScriptService = setupCompleteScriptService;
    }

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
        string runnerPath = Path.Combine(preOobeRoot, RunnerFileName);
        string manifestPath = Path.Combine(preOobeRoot, ManifestFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(preOobeRoot);
        Directory.CreateDirectory(scriptsRoot);

        string[] stagedScriptPaths = StageScripts(scriptsRoot, orderedScripts);
        File.WriteAllText(runnerPath, BuildRunner(orderedScripts), Utf8NoBom);
        File.WriteAllText(manifestPath, BuildManifest(orderedScripts), Utf8NoBom);

        _setupCompleteScriptService.RemoveBlock(setupCompletePath, "FOUNDRY DRIVERPACK");
        _setupCompleteScriptService.EnsureBlock(
            setupCompletePath,
            SetupCompleteMarkerKey,
            $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{RuntimePreOobeRoot}\\{RunnerFileName}\"");

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
                .ToArray()
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

    private static string BuildRunner(IReadOnlyList<PreOobeScriptDefinition> orderedScripts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine("$preOobeRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\PreOobe'");
        builder.AppendLine("$scriptsRoot = Join-Path $preOobeRoot 'Scripts'");
        builder.AppendLine("$logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\PreOobe'");
        builder.AppendLine("New-Item -Path $logRoot -ItemType Directory -Force | Out-Null");
        builder.AppendLine();
        builder.AppendLine("function Invoke-FoundryScript {");
        builder.AppendLine("    param(");
        builder.AppendLine("        [Parameter(Mandatory = $true)]");
        builder.AppendLine("        [string]$ScriptPath,");
        builder.AppendLine("        [string[]]$Arguments = @()");
        builder.AppendLine("    )");
        builder.AppendLine();
        builder.AppendLine("    $name = [System.IO.Path]::GetFileNameWithoutExtension($ScriptPath)");
        builder.AppendLine("    $logPath = Join-Path $logRoot \"$name.log\"");
        builder.AppendLine("    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments *> $logPath");
        builder.AppendLine("    if ($LASTEXITCODE -ne 0) {");
        builder.AppendLine("        throw \"Pre-OOBE script '$ScriptPath' failed with exit code $LASTEXITCODE. See '$logPath'.\"");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        foreach (PreOobeScriptDefinition script in orderedScripts)
        {
            builder.Append("Invoke-FoundryScript -ScriptPath (Join-Path $scriptsRoot ");
            builder.Append(ToPowerShellString(script.FileName));
            builder.Append(") -Arguments ");
            builder.AppendLine(ToPowerShellArray(script.Arguments));
        }

        return builder.ToString();
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
                arguments = script.Arguments
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
}

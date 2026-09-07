// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Stages restricted first-boot inputs and the independently journaled action runner.</summary>
public sealed class PreOobeScriptProvisioningService : IPreOobeScriptProvisioningService
{
    private const string Marker = "FOUNDRY PRE-OOBE";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ISetupCompleteScriptService setupComplete;
    private readonly Action<string> createRestrictedDirectory;
    private readonly Action<string> verifyRestrictedFile;

    /// <summary>Creates the production staging policy.</summary>
    public PreOobeScriptProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
        : this(setupCompleteScriptService, SensitiveStagingPolicy.CreateRestrictedDirectory, SensitiveStagingPolicy.VerifyRestrictedFile) { }

    internal PreOobeScriptProvisioningService(ISetupCompleteScriptService setupCompleteScriptService,
        Action<string> createRestrictedDirectory, Action<string> verifyRestrictedFile)
    {
        setupComplete = setupCompleteScriptService;
        this.createRestrictedDirectory = createRestrictedDirectory;
        this.verifyRestrictedFile = verifyRestrictedFile;
    }

    /// <inheritdoc />
    public PreOobeScriptProvisioningResult Provision(string targetWindowsPartitionRoot,
        IEnumerable<PreOobeScriptDefinition> scripts, FirstBootExecutionPlan executionPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWindowsPartitionRoot);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(executionPlan);
        FirstBootEntryPoint entry = executionPlan.CustomizationEntryPoint;
        if (executionPlan?.FailureCode is not null || entry is not (FirstBootEntryPoint.SetupComplete or FirstBootEntryPoint.GeneratedSpecialize or FirstBootEntryPoint.VerifiedCustomSpecialize))
            throw new InvalidOperationException(executionPlan?.FailureCode ?? "unsupported_setup_hook");
        PreOobeScriptDefinition[] ordered = scripts.Select(Normalize).GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).OrderBy(script => script.Priority).ThenBy(script => script.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new InvalidOperationException("At least one pre-OOBE script is required.");
        ValidateOwnership(ordered);
        string root = Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "PreOobe");
        string scriptsRoot = Path.Combine(root, "Scripts");
        string dataRoot = Path.Combine(root, "Data");
        string runner = Path.Combine(root, "Invoke-FoundryPreOobe.ps1");
        string manifest = Path.Combine(root, "pre-oobe-manifest.json");
        string results = Path.Combine(root, "results.json");
        string setup = Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
        createRestrictedDirectory(root);
        createRestrictedDirectory(scriptsRoot);
        createRestrictedDirectory(dataRoot);
        if (File.Exists(results))
        {
            verifyRestrictedFile(results);
            using JsonDocument existing = JsonDocument.Parse(File.ReadAllBytes(results));
            if (existing.RootElement.ValueKind != JsonValueKind.Array || existing.RootElement.EnumerateArray()
                .Any(result => result.GetProperty("status").GetString() != "staged" || result.GetProperty("attempt").GetInt32() != 0))
                throw new InvalidOperationException("First-boot execution already started; use explicit action retry instead of restaging.");
            if (File.Exists(manifest))
            {
                verifyRestrictedFile(manifest);
                using JsonDocument previous = JsonDocument.Parse(File.ReadAllBytes(manifest));
                if (previous.RootElement.GetProperty("scripts").EnumerateArray().SelectMany(script => script.GetProperty("dataFiles").EnumerateArray())
                    .Any(file => file.GetProperty("isSensitive").GetBoolean()))
                    throw new InvalidOperationException("Existing secret staging requires explicit cleanup before restaging.");
            }
        }
        var staged = new List<string>();
        var writtenSecrets = new List<string>();
        try
        {
            foreach (PreOobeScriptDefinition script in ordered)
            {
                string scriptPath = Path.Combine(scriptsRoot, script.FileName);
                WriteProtected(scriptPath, ReadResource(script.ResourceName));
                staged.Add(scriptPath);
                foreach (PreOobeScriptDataFile file in script.DataFiles)
                {
                    string path = Path.Combine(dataRoot, file.FileName);
                    createRestrictedDirectory(Path.GetDirectoryName(path)!);
                    bool secret = file.CleanupDisposition == PreOobeCleanupDisposition.SecretAlways;
                    WriteProtected(path, file.Bytes ?? Utf8NoBom.GetBytes(file.Content), secret ? writtenSecrets : null);
                }
            }
            WriteProtected(runner, ReadResource(PreOobeScriptResources.Runner));
            WriteProtected(Path.Combine(root, "Foundry-PreOobeFunctions.ps1"), ReadResource(PreOobeScriptResources.Functions));
            WriteProtected(manifest, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                scripts = ordered.Select(script => new
                {
                    id = script.Id,
                    fileName = script.FileName,
                    priority = (int)script.Priority,
                    arguments = script.Arguments,
                    dependsOn = script.DependsOn,
                    dataFiles = script.DataFiles.Select(file => new
                    {
                        fileName = file.FileName,
                        owningActionId = file.OwningActionId,
                        cleanupDisposition = file.CleanupDisposition.ToString(),
                        isSensitive = file.IsSensitive
                    })
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
            WriteProtected(results, JsonSerializer.SerializeToUtf8Bytes(ordered.Select(script => new PreOobeActionResult(
                script.Id, "staged", null, false, 0, DateTimeOffset.UtcNow, null, null)),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            setupComplete.RemoveBlock(setup, "FOUNDRY DRIVERPACK");
            if (entry == FirstBootEntryPoint.SetupComplete)
                setupComplete.EnsureBlock(setup, Marker, BuildSetupCompleteLauncher());
            else setupComplete.RemoveBlock(setup, Marker);
            return new()
            {
                SetupCompletePath = entry == FirstBootEntryPoint.SetupComplete ? setup : string.Empty,
                EntryPoint = entry,
                RunnerPath = runner,
                ManifestPath = manifest,
                ResultsPath = results,
                StagedScriptPaths = staged
            };
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();
            foreach (string secretPath in writtenSecrets)
            {
                try
                {
                    SensitiveStagingPolicy.RejectReparsePoints(secretPath);
                    File.Delete(secretPath);
                }
                catch (Exception cleanupFailure) { cleanupFailures.Add(cleanupFailure); }
            }
            if (cleanupFailures.Count > 0) failure.Data["SecretCleanupFailures"] = new AggregateException(cleanupFailures);
            throw;
        }
    }

    internal PreOobeScriptProvisioningResult Provision(string root, IEnumerable<PreOobeScriptDefinition> scripts) =>
        Provision(root, scripts, new FirstBootExecutionPlan(FirstBootEntryPoint.SetupComplete, false, null));

    private void WriteProtected(string path, byte[] bytes, List<string>? writtenSecrets = null)
    {
        SensitiveStagingPolicy.RejectReparsePoints(path);
        if (File.Exists(path)) verifyRestrictedFile(path);
        using var output = new FileStream(path, writtenSecrets is null ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        writtenSecrets?.Add(path);
        verifyRestrictedFile(path);
        output.Write(bytes);
        output.Flush(true);
    }

    private static byte[] ReadResource(string name)
    {
        using Stream stream = typeof(PreOobeScriptProvisioningService).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded pre-OOBE script resource '{name}' was not found.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static PreOobeScriptDefinition Normalize(PreOobeScriptDefinition script)
    {
        string id = script.Id.Trim();
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid first-boot action identifier.");
        string fileName = NormalizeRelativePath(script.FileName);
        if (fileName != Path.GetFileName(fileName)) throw new ArgumentException("A script name must not contain a path.");
        ArgumentException.ThrowIfNullOrWhiteSpace(script.ResourceName);
        return script with
        {
            Id = id,
            FileName = fileName,
            DataFiles = script.DataFiles.Select(file => file with
            {
                FileName = NormalizeRelativePath(file.FileName),
                OwningActionId = string.IsNullOrEmpty(file.OwningActionId) ? id : file.OwningActionId
            }).ToArray()
        };
    }

    private static string NormalizeRelativePath(string value)
    {
        string[] parts = value.Replace('/', '\\').Split('\\');
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
            parts.Any(part => string.IsNullOrEmpty(part) || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Invalid staged relative path.");
        return Path.Combine(parts);
    }

    private static void ValidateOwnership(IReadOnlyList<PreOobeScriptDefinition> scripts)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = scripts.Select(script => script.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (PreOobeScriptDefinition script in scripts)
        {
            if (script.DependsOn.Any(id => !ids.Contains(id) || id == script.Id)) throw new ArgumentException("Invalid action dependency.");
            foreach (PreOobeScriptDataFile file in script.DataFiles)
                if (file.OwningActionId != script.Id || !paths.Add(file.FileName) ||
                    (file.IsSensitive && file.CleanupDisposition != PreOobeCleanupDisposition.SecretAlways))
                    throw new ArgumentException("Invalid input ownership or secret cleanup policy.");
        }
    }

    private static string BuildSetupCompleteLauncher() => string.Join(Environment.NewLine,
    [
        "mkdir \"%SystemRoot%\\Temp\\Foundry\\Logs\\PreOobe\" >nul 2>&1",
        "\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%SystemRoot%\\Temp\\Foundry\\PreOobe\\Invoke-FoundryPreOobe.ps1\" >>\"%SystemRoot%\\Temp\\Foundry\\Logs\\PreOobe\\SetupComplete.log\" 2>&1",
        "set \"FOUNDRY_PREOOBE_EXIT=%ERRORLEVEL%\"",
        "exit /b %FOUNDRY_PREOOBE_EXIT%"
    ]);
}

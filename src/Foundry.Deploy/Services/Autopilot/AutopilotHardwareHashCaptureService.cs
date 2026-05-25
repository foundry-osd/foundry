using System.IO;
using System.Text;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Orchestrates the local WinPE OA3Tool capture workflow and retains troubleshooting artifacts.
/// </summary>
public sealed class AutopilotHardwareHashCaptureService(
    IProcessRunner processRunner,
    ILogger<AutopilotHardwareHashCaptureService> logger) : IAutopilotHardwareHashCaptureService
{
    private const string PcpKspFileName = "PCPKsp.dll";
    private const string Oa3ToolRelativePath = @"Tools\OA3\oa3tool.exe";
    private const string RuntimeHashRelativePath = @"Runtime\AutopilotHash";
    private const string Oa3ConfigFileName = "OA3.cfg";
    private const string Oa3InputFileName = "input.xml";
    private const string Oa3XmlFileName = "OA3.xml";
    private const string Oa3LogFileName = "OA3.log";
    private const string CsvFileName = "AutopilotHWID.csv";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly string Oa3ConfigContent = """
        [OA3Tool]
        InputXML=input.xml
        OutputXML=OA3.xml
        """;

    private static readonly string Oa3InputContent = """
        <?xml version="1.0" encoding="utf-8"?>
        <Key />
        """;

    /// <inheritdoc />
    public async Task<AutopilotHardwareHashCaptureResult> CaptureAsync(
        AutopilotHardwareHashCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string runtimeHashRoot = Path.Combine(request.WorkspaceRootPath, RuntimeHashRelativePath);
        string oa3ToolPath = Path.Combine(request.WorkspaceRootPath, Oa3ToolRelativePath);
        string sourcePcpKspPath = Path.Combine(request.TargetWindowsRootPath, "Windows", "System32", PcpKspFileName);
        string destinationPcpKspPath = Path.Combine(request.WinPeWindowsRootPath, "System32", PcpKspFileName);

        Directory.CreateDirectory(runtimeHashRoot);
        Directory.CreateDirectory(request.DiagnosticsRootPath);

        if (!File.Exists(oa3ToolPath))
        {
            return AutopilotHardwareHashCaptureResult.Failed(
                AutopilotHardwareHashCaptureFailureCode.ToolMissing,
                $"OA3Tool was not found at '{oa3ToolPath}'.");
        }

        if (!File.Exists(sourcePcpKspPath))
        {
            return AutopilotHardwareHashCaptureResult.Failed(
                AutopilotHardwareHashCaptureFailureCode.SupportLibraryMissing,
                $"Required support library was not found at '{sourcePcpKspPath}'.");
        }

        try
        {
            string? destinationDirectory = Path.GetDirectoryName(destinationPcpKspPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePcpKspPath, destinationPcpKspPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to copy PCPKsp.dll into the active WinPE System32 folder.");
            return AutopilotHardwareHashCaptureResult.Failed(
                AutopilotHardwareHashCaptureFailureCode.SupportLibraryCopyFailed,
                $"Required support library could not be copied to '{destinationPcpKspPath}': {ex.Message}");
        }

        await WriteOa3InputsAsync(runtimeHashRoot, cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult execution = await processRunner
            .RunAsync(
                oa3ToolPath,
                ["/Report", "/ConfigFile=.\\OA3.cfg", "/NoKeyCheck", "/LogTrace=.\\OA3.log"],
                runtimeHashRoot,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            return AutopilotHardwareHashCaptureResult.Failed(
                ResolveToolFailureCode(execution),
                BuildProcessFailureMessage(execution));
        }

        string oa3XmlPath = Path.Combine(runtimeHashRoot, Oa3XmlFileName);
        if (!File.Exists(oa3XmlPath))
        {
            return AutopilotHardwareHashCaptureResult.Failed(
                AutopilotHardwareHashCaptureFailureCode.ReportMissing,
                "OA3Tool did not create OA3.xml.");
        }

        string oa3Xml = await File.ReadAllTextAsync(oa3XmlPath, cancellationToken).ConfigureAwait(false);
        AutopilotHardwareHashParseResult parseResult = AutopilotOa3XmlParser.Parse(oa3Xml);
        if (!parseResult.IsSuccess || parseResult.Identity is null)
        {
            return AutopilotHardwareHashCaptureResult.Failed(parseResult.FailureCode, parseResult.Message);
        }

        AutopilotHardwareHashDeviceIdentity identity = parseResult.Identity with
        {
            GroupTag = request.GroupTag
        };
        string csvPath = Path.Combine(runtimeHashRoot, CsvFileName);
        await AutopilotHardwareHashCsvWriter.WriteAsync(csvPath, identity, cancellationToken).ConfigureAwait(false);

        string retainedOa3XmlPath = await CopyIfExistsAsync(oa3XmlPath, request.DiagnosticsRootPath, cancellationToken).ConfigureAwait(false);
        string? retainedOa3LogPath = await CopyOptionalAsync(
            Path.Combine(runtimeHashRoot, Oa3LogFileName),
            request.DiagnosticsRootPath,
            cancellationToken).ConfigureAwait(false);
        string retainedCsvPath = await CopyIfExistsAsync(csvPath, request.DiagnosticsRootPath, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Autopilot hardware hash captured. DiagnosticsPath={DiagnosticsPath}, SerialNumber={SerialNumber}.",
            request.DiagnosticsRootPath,
            identity.SerialNumber);

        return AutopilotHardwareHashCaptureResult.Succeeded(
            identity,
            retainedOa3XmlPath,
            retainedOa3LogPath,
            retainedCsvPath);
    }

    private static async Task WriteOa3InputsAsync(string runtimeHashRoot, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(runtimeHashRoot, Oa3ConfigFileName),
            Oa3ConfigContent,
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(runtimeHashRoot, Oa3InputFileName),
            Oa3InputContent,
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> CopyIfExistsAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        string? copiedPath = await CopyOptionalAsync(sourcePath, destinationDirectory, cancellationToken).ConfigureAwait(false);
        return copiedPath ?? sourcePath;
    }

    private static async Task<string?> CopyOptionalAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        await using FileStream source = File.OpenRead(sourcePath);
        await using FileStream destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return destinationPath;
    }

    private static string BuildProcessFailureMessage(ProcessExecutionResult execution)
    {
        string details = string.IsNullOrWhiteSpace(execution.StandardError)
            ? execution.StandardOutput
            : execution.StandardError;
        return string.IsNullOrWhiteSpace(details)
            ? $"OA3Tool exited with code {execution.ExitCode}."
            : $"OA3Tool exited with code {execution.ExitCode}: {details.Trim()}";
    }

    private static AutopilotHardwareHashCaptureFailureCode ResolveToolFailureCode(ProcessExecutionResult execution)
    {
        string combinedOutput = string.Concat(execution.StandardOutput, " ", execution.StandardError);
        return combinedOutput.Contains(PcpKspFileName, StringComparison.OrdinalIgnoreCase) ||
               combinedOutput.Contains("load", StringComparison.OrdinalIgnoreCase) &&
               combinedOutput.Contains("provider", StringComparison.OrdinalIgnoreCase)
            ? AutopilotHardwareHashCaptureFailureCode.SupportLibraryLoadFailed
            : AutopilotHardwareHashCaptureFailureCode.ToolFailed;
    }
}

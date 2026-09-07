// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeToolResolver
{
    private const string AdkRegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";
    private const string AdkRegistryKey = "KitsRoot10";

    private readonly Func<string?> _readKitsRootFromRegistry;
    private readonly Func<Architecture> _getHostArchitecture;
    private readonly Func<string, Version> _getFileVersion;

    public WinPeToolResolver()
        : this(ReadKitsRootFromRegistry)
    {
    }

    internal WinPeToolResolver(Func<string?> readKitsRootFromRegistry,
        Func<Architecture>? getHostArchitecture = null, Func<string, Version>? getFileVersion = null)
    {
        _readKitsRootFromRegistry = readKitsRootFromRegistry;
        _getHostArchitecture = getHostArchitecture ?? (() => RuntimeInformation.OSArchitecture);
        _getFileVersion = getFileVersion ?? ReadFileVersion;
    }

    /// <summary>Resolves host-native installed ADK tools independently of the target boot image architecture.</summary>
    public WinPeResult<WinPeToolPaths> ResolveTools(string? kitsRootOverride = null, WinPeArchitecture targetArchitecture = WinPeArchitecture.X64)
    {
        string? kitsRoot = NormalizeKitsRoot(kitsRootOverride);
        if (string.IsNullOrWhiteSpace(kitsRoot))
        {
            kitsRoot = NormalizeKitsRoot(_readKitsRootFromRegistry());
        }

        if (string.IsNullOrWhiteSpace(kitsRoot))
        {
            return WinPeResult<WinPeToolPaths>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Could not locate Windows ADK KitsRoot10.",
                "Install ADK + WinPE add-on or provide an explicit ADK root path.",
                toolName: "Windows ADK");
        }

        string[] winPeRootCandidates =
        [
            Path.Combine(kitsRoot, "Assessment and Deployment Kit", "Windows Preinstallation Environment"),
            Path.Combine(kitsRoot, "Windows Preinstallation Environment")
        ];

        string? copypePath = ResolveToolPath(winPeRootCandidates, "copype.cmd");
        string? makeWinPeMediaPath = ResolveToolPath(winPeRootCandidates, "MakeWinPEMedia.cmd");

        if (copypePath is null || makeWinPeMediaPath is null)
        {
            return WinPeResult<WinPeToolPaths>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Required WinPE ADK tools were not found.",
                $"Expected copype.cmd and MakeWinPEMedia.cmd under '{kitsRoot}'.",
                toolName: "copype/MakeWinPEMedia");
        }

        Architecture hostArchitecture = _getHostArchitecture();
        if (hostArchitecture is not (Architecture.X64 or Architecture.Arm64) || !Enum.IsDefined(targetArchitecture))
        {
            return WinPeResult<WinPeToolPaths>.Failure(WinPeErrorCodes.ToolNotFound,
                "The host or target architecture is unsupported for ADK servicing.",
                $"Host={hostArchitecture}; Target={targetArchitecture}.", toolName: "dism");
        }
        string hostFolder = hostArchitecture == Architecture.X64 ? "amd64" : "arm64";
        string dismPath = Path.Combine(kitsRoot, "Assessment and Deployment Kit", "Deployment Tools", hostFolder, "DISM", "dism.exe");
        if (!File.Exists(dismPath))
        {
            dismPath = Path.Combine(kitsRoot, "Deployment Tools", hostFolder, "DISM", "dism.exe");
        }
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string cmdPath = Path.Combine(windowsDirectory, "System32", "cmd.exe");

        if (!File.Exists(dismPath))
        {
            return WinPeResult<WinPeToolPaths>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Host-native Windows ADK DISM was not found. Install compatible ADK Deployment Tools.",
                $"Expected path: '{dismPath}'.",
                toolName: "dism");
        }

        Version dismVersion;
        try
        {
            WinPeExecutableArchitecture.ValidateNative(dismPath,
                hostArchitecture == Architecture.X64 ? WinPeArchitecture.X64 : WinPeArchitecture.Arm64);
            dismVersion = _getFileVersion(dismPath);
            if (dismVersion.Major != 10 || dismVersion.Minor != 0 || dismVersion.Build < 26100)
            {
                throw new InvalidDataException($"Foundry requires Windows 11 ADK DISM build 26100 or later; found {dismVersion}.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return WinPeResult<WinPeToolPaths>.Failure(WinPeErrorCodes.ToolNotFound,
                "Windows ADK DISM is incompatible or unreadable.", ex.Message, toolName: "dism");
        }

        if (!File.Exists(cmdPath))
        {
            return WinPeResult<WinPeToolPaths>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "cmd.exe was not found.",
                $"Expected path: '{cmdPath}'.",
                toolName: "cmd");
        }

        return WinPeResult<WinPeToolPaths>.Success(new WinPeToolPaths
        {
            KitsRootPath = kitsRoot,
            CopypePath = copypePath,
            MakeWinPeMediaPath = makeWinPeMediaPath,
            DismPath = dismPath,
            HostArchitecture = hostArchitecture,
            TargetArchitecture = targetArchitecture,
            DismVersion = dismVersion,
            CmdPath = cmdPath,
            PowerShellPath = "powershell.exe"
        });
    }

    private static Version ReadFileVersion(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        return new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart);
    }

    /// <summary>Queries an exact source index with the selected ADK before servicing its Windows 11 image.</summary>
    internal static async Task<WinPeResult> ValidateImageAsync(
        WinPeToolPaths tools, string imagePath, int index, WinPeArchitecture target,
        IWinPeProcessRunner runner, string workingDirectory, CancellationToken cancellationToken, bool writeDiagnosticLog = true)
    {
        WinPeProcessExecution result = await runner.RunAsync(tools.DismPath,
            ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}", $"/Index:{index}"],
            workingDirectory, cancellationToken, executionTimeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        if (!result.IsSuccess)
        {
            return WinPeResult.Failure(result.ToFailureDiagnostic(WinPeErrorCodes.BuildFailed,
                "Failed to inspect source image compatibility.", "Validate source image", "dism"));
        }
        WinPeResult compatibility = ValidateImageMetadata(tools, target, result.StandardOutput);
        if (!writeDiagnosticLog)
        {
            return compatibility;
        }

        string logDirectory = Path.Combine(workingDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        await File.AppendAllTextAsync(Path.Combine(logDirectory, "source-image-validation.log"),
            $"{DateTime.UtcNow:O} Host={tools.HostArchitecture}; DISM={tools.DismVersion}; Image={ReadMetadataField(result.StandardOutput, "Version")}; " +
            $"ImageRevision={ReadMetadataField(result.StandardOutput, "ServicePack Build")}; Architecture={ReadMetadataField(result.StandardOutput, "Architecture")}; " +
            $"Target={target}; Compatible={compatibility.IsSuccess}; Source={imagePath}; Index={index}; Tool={tools.DismPath}.{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
        return compatibility;
    }

    /// <summary>Checks documented Windows 11 servicing families and architecture; update revisions are recorded, not compared.</summary>
    internal static WinPeResult ValidateImageMetadata(WinPeToolPaths tools, WinPeArchitecture target, string output)
    {
        string architecture = ReadMetadataField(output, "Architecture");
        string versionText = ReadMetadataField(output, "Version");
        string expectedArchitecture = target == WinPeArchitecture.X64 ? "x64" : "arm64";
        // Windows 11 25H2 (KB5054156) shares the 24H2 servicing base; this does not imply support for future families.
        bool supported = Enum.IsDefined(target) && tools.TargetArchitecture == target &&
            architecture.Equals(expectedArchitecture, StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(versionText, out Version? version) && version.Major == 10 && version.Minor == 0 &&
            version.Build is 22000 or 22621 or 22631 or 26100 or 26200 &&
            tools.DismVersion.Major == 10 && tools.DismVersion.Minor == 0 && tools.DismVersion.Build >= 26100 &&
            tools.DismVersion.Build >= (version.Build == 26200 ? 26100 : version.Build);
        if (!supported)
        {
            return WinPeResult.Failure(WinPeErrorCodes.ValidationFailed,
                "The source image is incompatible with the selected Windows 11 ADK DISM or target architecture.",
                $"Host={tools.HostArchitecture}; DISM={tools.DismVersion}; Image={versionText}; ImageArchitecture={architecture}; Target={target}; Tool={tools.DismPath}.");
        }
        return WinPeResult.Success();
    }

    private static string ReadMetadataField(string output, string field)
    {
        // DISM prints its own version before the selected image's architecture and version.
        if (field == "Version")
        {
            Match architecture = Regex.Match(output, @"^[\t ]*Architecture[\t ]*:", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (!architecture.Success)
            {
                return string.Empty;
            }
            output = output[architecture.Index..];
        }
        MatchCollection matches = Regex.Matches(output, $@"^[\t ]*{Regex.Escape(field)}[\t ]*:[\t ]*([^\r\n]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return matches.Count == 0 ? string.Empty : matches[^1].Groups[1].Value.Trim();
    }

    public async Task<bool> IsBootExSupportedAsync(
        WinPeToolPaths toolPaths,
        IWinPeProcessRunner processRunner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution helpResult = await processRunner.RunCmdScriptDirectAsync(
            toolPaths.MakeWinPeMediaPath,
            "/?",
            workingDirectory,
            cancellationToken,
            executionTimeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        helpResult.EnsureCompleteOutput();
        string combined = string.Concat(helpResult.StandardOutput, "\n", helpResult.StandardError);
        return combined.IndexOf("/bootex", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ReadKitsRootFromRegistry()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AdkRegistryPath);
            return key?.GetValue(AdkRegistryKey) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeKitsRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string normalized = candidate.Trim().Trim('"');
        if (!Directory.Exists(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string? ResolveToolPath(IEnumerable<string> rootCandidates, string fileName)
    {
        foreach (string candidateRoot in rootCandidates)
        {
            if (!Directory.Exists(candidateRoot))
            {
                continue;
            }

            string directPath = Path.Combine(candidateRoot, fileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            string[] nestedCandidates = Directory.GetFiles(candidateRoot, fileName, SearchOption.AllDirectories);
            if (nestedCandidates.Length > 0)
            {
                return nestedCandidates[0];
            }
        }

        return null;
    }
}

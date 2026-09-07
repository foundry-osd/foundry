// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Verifies offline WinPE package state before a customized image can be committed.</summary>
public sealed class WinPeCapabilityValidationService
{
    private readonly IWinPeProcessRunner _processRunner;

    public WinPeCapabilityValidationService() : this(new WinPeProcessRunner()) { }

    internal WinPeCapabilityValidationService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <summary>Requires installed neutral components, matching language resources and selected CJK fonts.</summary>
    public async Task<WinPeResult<WinPeCapabilityValidationResult>> ValidateAsync(
        WinPeImageInternationalizationOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WinPeDiagnostic? error = WinPeImageInternationalizationService.ValidateOptions(options);
        if (error is not null)
        {
            return WinPeResult<WinPeCapabilityValidationResult>.Failure(error);
        }
        if (!WinPeLanguageUtility.TryResolveInputLocale(options.WinPeLanguage, out _, out _))
        {
            return WinPeResult<WinPeCapabilityValidationResult>.Failure(WinPeErrorCodes.ValidationFailed, "The WinPE language is not a supported specific locale.");
        }

        WinPeResult<IReadOnlyList<InstalledPackage>> inventory = await ReadInventoryAsync(options, cancellationToken).ConfigureAwait(false);
        if (!inventory.IsSuccess)
        {
            return WinPeResult<WinPeCapabilityValidationResult>.Failure(inventory.Error!);
        }

        IReadOnlyList<RequiredPackage> required = GetRequiredPackages(options.WinPeLanguage);
        var verified = new List<string>();
        foreach (RequiredPackage package in required)
        {
            InstalledPackage? installed = FindInstalled(inventory.Value!, package, options.Architecture);
            if (installed is null)
            {
                return WinPeResult<WinPeCapabilityValidationResult>.Failure(
                    WinPeErrorCodes.BuildFailed, $"The required WinPE package '{package.Key}' is not installed.",
                    "Required packages must be in the Installed state with the target architecture and matching neutral/language versions.");
            }
            verified.Add(installed.Identity);
        }
        return WinPeResult<WinPeCapabilityValidationResult>.Success(new(required.Select(package => package.Key).ToArray(), verified));
    }

    /// <summary>Reads bounded English DISM records; missing or conflicting states require package-specific confirmation.</summary>
    internal async Task<WinPeResult<IReadOnlyList<InstalledPackage>>> ReadInventoryAsync(
        WinPeImageInternationalizationOptions options, CancellationToken cancellationToken)
    {
        WinPeProcessExecution execution = await _processRunner.RunAsync(options.Tools!.DismPath,
            ["/English", $"/Image:{options.MountedImagePath}", "/Get-Packages", "/Format:List"],
            options.WorkingDirectoryPath, cancellationToken, executionTimeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (!TryReadExecution(execution, out List<InstalledPackage> packages))
        {
            return InventoryFailure(execution);
        }

        var resolved = new List<InstalledPackage>();
        foreach (IGrouping<string, InstalledPackage> group in packages.GroupBy(package => package.Identity, StringComparer.OrdinalIgnoreCase))
        {
            InstalledPackage package = group.First();
            if (string.IsNullOrWhiteSpace(package.State) || group.Any(entry => entry.State != package.State))
            {
                WinPeProcessExecution detail = await _processRunner.RunAsync(options.Tools.DismPath,
                    ["/English", $"/Image:{options.MountedImagePath}", "/Get-PackageInfo", $"/PackageName:{package.Identity}"],
                    options.WorkingDirectoryPath, cancellationToken, executionTimeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                if (!TryReadExecution(detail, out List<InstalledPackage> details) || details.Count != 1 ||
                    !string.Equals(details[0].Identity, package.Identity, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(details[0].State))
                {
                    return InventoryFailure(detail);
                }
                package = details[0];
            }
            resolved.Add(package);
        }
        return WinPeResult<IReadOnlyList<InstalledPackage>>.Success(resolved);
    }

    /// <summary>Maps only exact Microsoft package identity fields, never substring names or successful command prose.</summary>
    internal static InstalledPackage? FindInstalled(IReadOnlyList<InstalledPackage> inventory, RequiredPackage required, WinPeArchitecture architecture)
    {
        return inventory.FirstOrDefault(package =>
            package.State == "Installed" &&
            string.Equals(package.Publisher, "31bf3856ad364e35", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Name, required.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Architecture, architecture.ToCopypeArchitecture(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Language, required.Language, StringComparison.OrdinalIgnoreCase) &&
            (required.Language.Length == 0 || required.Name == "Microsoft-Windows-WinPE-LanguagePack-Package" ||
             inventory.Any(neutral => neutral.State == "Installed" && neutral.Language.Length == 0 &&
                 string.Equals(neutral.Publisher, package.Publisher, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(neutral.Name, package.Name, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(neutral.Architecture, package.Architecture, StringComparison.OrdinalIgnoreCase) && neutral.Version == package.Version)));
    }

    /// <summary>Preserves prerequisite order. These ten ADK components have language satellites; font packages do not.</summary>
    internal static IReadOnlyList<RequiredPackage> GetRequiredPackages(string language)
    {
        string locale = WinPeLanguageUtility.Normalize(language);
        var required = new List<RequiredPackage>
        {
            new("Microsoft-Windows-WinPE-LanguagePack-Package", locale, Path.Combine(locale, "lp.cab"))
        };
        string[] components =
        [
            "WinPE-WMI", "WinPE-NetFX", "WinPE-Scripting", "WinPE-PowerShell", "WinPE-WinReCfg",
            "WinPE-DismCmdlets", "WinPE-StorageWMI", "WinPE-Dot3Svc", "WinPE-EnhancedStorage", "WinPE-SecureStartup"
        ];
        foreach (string component in components)
        {
            required.Add(new($"{component}-Package", string.Empty, $"{component}.cab"));
            required.Add(new($"{component}-Package", locale, Path.Combine(locale, $"{component}_{locale}.cab")));
        }
        string? font = locale switch
        {
            "ja-jp" => "JA-JP",
            "ko-kr" => "KO-KR",
            "zh-cn" or "zh-sg" => "ZH-CN",
            "zh-tw" => "ZH-TW",
            "zh-hk" or "zh-mo" => "ZH-HK",
            _ => null
        };
        if (font is not null)
        {
            required.Add(new($"WinPE-FontSupport-{font}-Package", string.Empty, $"WinPE-FontSupport-{font}.cab"));
        }
        return required;
    }

    private static WinPeResult<IReadOnlyList<InstalledPackage>> InventoryFailure(WinPeProcessExecution execution) =>
        WinPeResult<IReadOnlyList<InstalledPackage>>.Failure(WinPeErrorCodes.BuildFailed,
            "The installed WinPE package inventory could not be verified.", execution.ToDiagnosticText());

    private static bool TryReadExecution(WinPeProcessExecution execution, out List<InstalledPackage> packages)
    {
        packages = [];
        if (!execution.IsSuccess || execution.StandardOutputTruncated || execution.StandardErrorTruncated ||
            execution.StandardOutput.Length > 4 * 1024 * 1024)
        {
            return false;
        }

        InstalledPackage? current = null;
        using var reader = new StringReader(execution.StandardOutput);
        while (reader.ReadLine() is string line)
        {
            if (line.Length > 4096 || packages.Count >= 4096)
            {
                return false;
            }
            int separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (key == "Package Identity")
            {
                if (current is not null)
                {
                    packages.Add(current);
                }
                string[] fields = value.Split('~');
                if (fields.Length != 5 || fields[0].Length == 0 || fields[1].Length != 16 || !fields[1].All(Uri.IsHexDigit) ||
                    fields[2].Length == 0 || !Version.TryParse(fields[4], out Version? version))
                {
                    return false;
                }
                current = new(value, fields[0], fields[1], fields[2], fields[3], version, string.Empty);
            }
            else if (key == "State")
            {
                if (current is null || current.State.Length > 0)
                {
                    return false;
                }
                current = current with { State = value };
            }
        }
        if (current is not null)
        {
            packages.Add(current);
        }
        return packages.Count > 0;
    }

    /// <summary>A semantic requirement is separate from the versioned identity reported by the mounted image.</summary>
    internal sealed record RequiredPackage(string Name, string Language, string RelativeCabPath)
    {
        public string Key => $"{Name}@{(Language.Length == 0 ? "neutral" : Language)}";
    }

    /// <summary>One complete package identity and its independently queried servicing state.</summary>
    internal sealed record InstalledPackage(string Identity, string Name, string Publisher, string Architecture, string Language, Version Version, string State);
}

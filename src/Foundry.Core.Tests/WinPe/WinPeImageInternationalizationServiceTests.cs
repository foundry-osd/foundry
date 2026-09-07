// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeImageInternationalizationServiceTests
{
    [Theory]
    [InlineData("es-ES")]
    [InlineData("fr-CA")]
    public async Task ApplyAsync_InstallsDependenciesAndUsesWindowsInputLocale(string language)
    {
        using var fixture = new InternationalizationFixture(language);
        WinPeResult result = await fixture.ApplyAsync();
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains(fixture.Runner.Calls, args => args.Contains($"/Set-InputLocale:{language}"));
        Assert.DoesNotContain(fixture.Runner.Calls.SelectMany(args => args), arg => arg.Contains("00000c0a", StringComparison.Ordinal));
        Assert.Equal(2, fixture.Runner.Calls.Count(args => args.Contains("/Get-Packages")));
        Assert.All(fixture.Runner.Calls.Where(args => args.Contains("/Get-Packages")), args =>
        {
            Assert.Contains("/English", args);
            Assert.Contains("/Format:List", args);
        });
        string[] additions = fixture.Runner.Calls.SelectMany(args => args).Where(arg => arg.StartsWith("/PackagePath:", StringComparison.Ordinal)).ToArray();
        Assert.True(Array.FindIndex(additions, arg => arg.EndsWith("WinPE-WMI.cab", StringComparison.Ordinal)) <
                    Array.FindIndex(additions, arg => arg.EndsWith("WinPE-PowerShell.cab", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("WinPE-PowerShell.cab")]
    [InlineData("en-us/WinPE-WMI_en-us.cab")]
    [InlineData("en-us/lp.cab")]
    public async Task ApplyAsync_MissingRequiredPackageBlocksSettings(string missing)
    {
        using var fixture = new InternationalizationFixture();
        File.Delete(Path.Combine(fixture.ComponentsRoot, missing));
        WinPeResult result = await fixture.ApplyAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
        Assert.DoesNotContain(fixture.Runner.Calls.SelectMany(args => args), arg => arg.StartsWith("/Set-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_InstalledWinReCapabilitiesDoNotRequireSourceCabs()
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        Directory.Delete(fixture.ComponentsRoot, recursive: true);
        WinPeResult result = await fixture.ApplyAsync();
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.DoesNotContain(fixture.Runner.Calls, args => args.Contains("/Add-Package"));
    }

    [Theory]
    [InlineData("not applicable", false)]
    [InlineData("already installed", false)]
    [InlineData("0x800f081e", true)]
    public async Task ApplyAsync_FailedAdditionRequiresIndependentInstalledEvidence(string output, bool installed)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.FailedComponent = "WinPE-PowerShell";
        fixture.Runner.FailureOutput = output;
        fixture.Runner.InstallFailedPackage = installed;
        WinPeResult result = await fixture.ApplyAsync();
        Assert.Equal(installed, result.IsSuccess);
    }

    [Theory]
    [InlineData("Install Pending")]
    [InlineData("Staged")]
    [InlineData("Not Present")]
    public async Task ApplyAsync_NonInstalledFinalStateBlocksSuccess(string state)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.PowerShellState = state;
        Assert.False((await fixture.ApplyAsync()).IsSuccess);
    }

    [Theory]
    [InlineData("ja-JP", "WinPE-FontSupport-JA-JP")]
    [InlineData("ko-KR", "WinPE-FontSupport-KO-KR")]
    [InlineData("zh-CN", "WinPE-FontSupport-ZH-CN")]
    [InlineData("zh-TW", "WinPE-FontSupport-ZH-TW")]
    [InlineData("zh-HK", "WinPE-FontSupport-ZH-HK")]
    public async Task ApplyAsync_CjkRequiresInstalledNeutralFontPackage(string language, string font)
    {
        using var fixture = new InternationalizationFixture(language, font);
        WinPeResult result = await fixture.ApplyAsync();
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains(fixture.Runner.Calls.SelectMany(args => args), arg => arg.EndsWith($"{font}.cab", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Runner.Calls.SelectMany(args => args), arg => arg.Contains($"{font}_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_MissingCjkFontBlocksSuccess()
    {
        using var fixture = new InternationalizationFixture("ja-JP");
        Assert.False((await fixture.ApplyAsync()).IsSuccess);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("truncated")]
    [InlineData("malformed")]
    public async Task ApplyAsync_UnreadableInventoryBlocksMutation(string mode)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InventoryFailure = mode;
        Assert.False((await fixture.ApplyAsync()).IsSuccess);
        Assert.DoesNotContain(fixture.Runner.Calls, args => args.Contains("/Add-Package"));
    }
}

internal sealed class InternationalizationFixture : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    internal static readonly string[] Components =
    [
        "WinPE-WMI", "WinPE-NetFX", "WinPE-Scripting", "WinPE-PowerShell", "WinPE-WinReCfg",
        "WinPE-DismCmdlets", "WinPE-StorageWMI", "WinPE-Dot3Svc", "WinPE-EnhancedStorage", "WinPE-SecureStartup"
    ];

    public InternationalizationFixture(string language = "en-US", string? font = null)
    {
        string locale = language.ToLowerInvariant();
        ComponentsRoot = Path.Combine(_directory.Path, "Assessment and Deployment Kit", "Windows Preinstallation Environment", "amd64", "WinPE_OCs");
        Directory.CreateDirectory(Path.Combine(ComponentsRoot, locale));
        Runner = new InternationalizationRunner();
        AddCab(Path.Combine(locale, "lp.cab"), "Microsoft-Windows-WinPE-LanguagePack-Package", locale);
        foreach (string component in Components)
        {
            AddCab($"{component}.cab", $"{component}-Package", string.Empty);
            AddCab(Path.Combine(locale, $"{component}_{locale}.cab"), $"{component}-Package", locale);
        }
        if (font is not null)
        {
            AddCab($"{font}.cab", $"{font}-Package", string.Empty);
        }
        Options = new WinPeImageInternationalizationOptions
        {
            MountedImagePath = _directory.Path,
            WorkingDirectoryPath = _directory.Path,
            WinPeLanguage = language,
            Architecture = WinPeArchitecture.X64,
            Tools = new WinPeToolPaths { KitsRootPath = _directory.Path, DismPath = "fixture-dism.exe" }
        };
    }

    public string ComponentsRoot { get; }
    public WinPeImageInternationalizationOptions Options { get; }
    public InternationalizationRunner Runner { get; }
    public Task<WinPeResult> ApplyAsync() => new WinPeImageInternationalizationService(Runner).ApplyAsync(Options, TestContext.Current.CancellationToken);
    public void Dispose() => _directory.Dispose();

    private void AddCab(string relativePath, string name, string language)
    {
        string path = Path.Combine(ComponentsRoot, relativePath);
        File.WriteAllText(path, string.Empty);
        Runner.CabIdentities[path] = $"{name}~31bf3856ad364e35~amd64~{language}~10.0.26100.1";
    }
}

internal sealed class InternationalizationRunner : IWinPeProcessRunner
{
    public Dictionary<string, string> CabIdentities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Installed { get; } = ["Microsoft-Windows-WinPE-Package~31bf3856ad364e35~amd64~~10.0.26100.1"];
    public List<string[]> Calls { get; } = [];
    public string? FailedComponent { get; set; }
    public string FailureOutput { get; set; } = string.Empty;
    public bool InstallFailedPackage { get; set; }
    public string PowerShellState { get; set; } = "Installed";
    public string? InventoryFailure { get; set; }
    public Func<string, string>? TransformInventory { get; set; }
    public string? PackageInfoOutput { get; set; }

    public void InstallAll() => Installed.AddRange(CabIdentities.Values);

    public Task<WinPeProcessExecution> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null, TimeSpan? executionTimeout = null)
    {
        Calls.Add(arguments.ToArray());
        int exitCode = 0;
        string output = string.Empty;
        if (arguments.Contains("/Get-Packages"))
        {
            exitCode = InventoryFailure == "failed" ? 1 : 0;
            output = InventoryFailure == "malformed" ? "Package Identity : invalid\nState : Installed" :
                "Deployment Image Servicing and Management tool\nVersion: 10.0.26100.1\n\nPackages listing:\n\n" +
                string.Join("\n\n", Installed.Select(identity => $"Package Identity : {identity}\nState : {(identity.StartsWith("WinPE-PowerShell-Package~", StringComparison.Ordinal) ? PowerShellState : "Installed")}\nRelease Type : Feature Pack\nInstall Time : 9/7/2026 10:00 AM")) +
                "\n\nThe operation completed successfully.";
            output = TransformInventory?.Invoke(output) ?? output;
        }
        else if (arguments.Contains("/Get-PackageInfo"))
        {
            output = PackageInfoOutput ?? string.Empty;
        }
        else if (arguments.Contains("/Add-Package"))
        {
            string path = arguments.Single(arg => arg.StartsWith("/PackagePath:", StringComparison.Ordinal))[13..];
            bool fails = FailedComponent is not null && Path.GetFileNameWithoutExtension(path) == FailedComponent;
            if (!fails || InstallFailedPackage)
            {
                Installed.Add(CabIdentities[path]);
            }
            exitCode = fails ? 1 : 0;
            output = fails ? FailureOutput : string.Empty;
        }
        return Task.FromResult(new WinPeProcessExecution
        {
            FileName = fileName,
            Arguments = string.Join(' ', arguments),
            WorkingDirectory = workingDirectory,
            ExitCode = exitCode,
            StandardOutput = output,
            StandardOutputTruncated = arguments.Contains("/Get-Packages") && InventoryFailure == "truncated"
        });
    }

    public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
    public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory,
        CancellationToken cancellationToken, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
    public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory,
        CancellationToken cancellationToken, TimeSpan? executionTimeout = null) => throw new NotSupportedException();
}

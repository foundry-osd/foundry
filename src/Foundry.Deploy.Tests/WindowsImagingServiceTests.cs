// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class WindowsImagingServiceTests
{
    [Fact]
    public async Task InspectImageAsync_ReturnsMatchingImageWithoutRequiringSetupMediaMetadata()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image.esd");
        await File.WriteAllTextAsync(imagePath, "owned fixture", TestContext.Current.CancellationToken);
        var runner = CreateInspectionRunner(WindowsImageInfoParserTests.Detail);
        IWindowsImageInspectionService service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        WindowsImageInfo image = await service.InspectImageAsync(imagePath, ImageSelection(), workspace.RootPath, TestContext.Current.CancellationToken);
        Assert.Equal(4, image.Index);
        Assert.Equal(new Version(10, 0, 26100, 1000), image.Version);
        Assert.Equal(3, runner.Calls.Count);
        Assert.All(runner.Calls, call => Assert.Contains("/Get-ImageInfo", call, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Edition : Professional", "Edition : Core")]
    [InlineData("Architecture : x64", "Architecture : x86")]
    [InlineData("Version : 10.0.26100", "Version : 10.0.26200")]
    [InlineData("Version : 10.0.26100", "Version : 11.0.26100")]
    [InlineData("ServicePack Build : 1000", "ServicePack Build : 1001")]
    [InlineData("en-US (Default)", "fr-FR (Default)")]
    [InlineData("Index : 4", "Index : 9")]
    public async Task InspectImageAsync_RejectsMismatchedActualImage(string original, string replacement)
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image.esd");
        await File.WriteAllTextAsync(imagePath, "owned fixture", TestContext.Current.CancellationToken);
        var runner = CreateInspectionRunner(WindowsImageInfoParserTests.Detail.Replace(original, replacement, StringComparison.Ordinal));
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        Exception? error = await Record.ExceptionAsync(() => service.InspectImageAsync(imagePath, ImageSelection(), workspace.RootPath, TestContext.Current.CancellationToken));
        Assert.True(error is InvalidDataException or InvalidOperationException);
        Assert.DoesNotContain(runner.Calls, call => call.Contains("/Apply-Image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InspectImageAsync_RejectsTruncatedSelectedDetails()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image.esd");
        await File.WriteAllTextAsync(imagePath, "owned fixture", TestContext.Current.CancellationToken);
        var runner = CreateInspectionRunner(WindowsImageInfoParserTests.Detail, truncated: true);
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectImageAsync(imagePath, ImageSelection(), workspace.RootPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectImageAsync_PropagatesCallerCancellationBeforeInvokingRunner()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runner = CreateInspectionRunner(WindowsImageInfoParserTests.Detail);
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectImageAsync("unused.esd", ImageSelection(), "unused", cancelled.Token));
        Assert.Empty(runner.Calls);
    }

    private static Foundry.Deploy.Models.OperatingSystemCatalogItem ImageSelection() => new()
    {
        Edition = "Pro",
        Architecture = "AMD64",
        BuildMajor = 26100,
        BuildUbr = 1000,
        LanguageCode = "en-US"
    };

    [Theory]
    [InlineData("Index : 4\nIndex : 4")]
    [InlineData("Index : 2147483648")]
    [InlineData("Index : -1")]
    public async Task InspectImageAsync_RejectsInvalidIndexSummary(string summary)
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image.esd");
        await File.WriteAllTextAsync(imagePath, "owned fixture", TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner { Result = new ProcessExecutionResult { ExitCode = 0, StandardOutput = summary } };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectImageAsync(imagePath, ImageSelection(), workspace.RootPath, TestContext.Current.CancellationToken));
        Assert.Single(runner.Calls);
    }

    private static RecordingProcessRunner CreateInspectionRunner(string detail, bool truncated = false) => new()
    {
        ResultFactory = arguments => arguments.Contains("/Index:4", StringComparison.Ordinal)
            ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = detail, StandardOutputTruncated = truncated }
            : new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = arguments.Contains("/Index:1", StringComparison.Ordinal)
                ? "Index : 1\nName : Windows Setup Media\nEdition : <undefined>\nArchitecture : <undefined>"
                : "Index : 1\nName : Windows Setup Media\nIndex : 4\nName : Windows OS"
            }
    };

    [Fact]
    public async Task ResolveImageIndexAsync_PreservesImagePathAsOneArgument()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image with spaces.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner { Result = new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 1\nEdition : Professional" } };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);

        int index = await service.ResolveImageIndexAsync(imagePath, "Pro", workspace.RootPath, TestContext.Current.CancellationToken);

        Assert.Equal(1, index);
        Assert.Equal(new[] { "/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}", "/Index:1" }, runner.LastArgumentTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveImageIndexAsync_RejectsTruncatedMetadata(bool truncateDetail)
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "image with spaces.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:", StringComparison.Ordinal)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 1\nEdition : Professional", StandardErrorTruncated = truncateDetail }
                : new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 1\nName : Windows 11 Pro", StandardOutputTruncated = !truncateDetail }
        };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ResolveImageIndexAsync(
            imagePath, "Pro", workspace.RootPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAppliedWindowsEditionAsync_RejectsTruncatedMetadata()
    {
        var runner = new RecordingProcessRunner
        {
            Result = new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Current Edition : Professional", StandardOutputTruncated = true }
        };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAppliedWindowsEditionAsync(
            @"W:\", Path.GetTempPath(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveImageIndexAsync_WhenRequestedEditionIsMissing_ThrowsBeforeImageApplication()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "consumer.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:4", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 4\nEdition : Core" }
                : arguments.Contains("/Index:9", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 9\nEdition : Professional" }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                    Index : 1
                    Name : Windows Setup Media

                    Index : 4
                    Name : Windows 11 Home

                    Index : 9
                    Name : Windows 11 Pro
                    """
                    }
        };
        var service = new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Enterprise",
                workspace.RootPath,
                TestContext.Current.CancellationToken));

        Assert.Contains("Enterprise", exception.Message, StringComparison.Ordinal);
        Assert.Contains("4: Core", exception.Message, StringComparison.Ordinal);
        Assert.Contains("9: Professional", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveImageIndexAsync_WhenSingleImageDoesNotMatchRequestedEdition_Throws()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "setup-media.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            Result = new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = """
                    Index : 1
                    Name : Windows Setup Media
                    """
            }
        };
        var service = new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Enterprise",
                workspace.RootPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveImageIndexAsync_DoesNotSelectNVariantForNonNEdition()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "consumer.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:5", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 5\nEdition : ProfessionalN" }
                : arguments.Contains("/Index:9", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 9\nEdition : Professional" }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                    Index : 5
                    Name : Windows 11 Pro N

                    Index : 9
                    Name : Windows 11 Pro
                    """
                    }
        };
        var service = new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance);

        int imageIndex = await service.ResolveImageIndexAsync(
            imagePath,
            "Pro",
            workspace.RootPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(9, imageIndex);
    }

    [Theory]
    [InlineData("Home", "Core", 4)]
    [InlineData("Home N", "CoreN", 5)]
    [InlineData("Home Single Language", "CoreSingleLanguage", 6)]
    [InlineData("Home China", "CoreCountrySpecific", 7)]
    [InlineData("Education", "Education", 8)]
    [InlineData("Education N", "EducationN", 9)]
    [InlineData("Pro", "Professional", 10)]
    [InlineData("Pro N", "ProfessionalN", 11)]
    [InlineData("Enterprise", "Enterprise", 12)]
    [InlineData("Enterprise N", "EnterpriseN", 13)]
    public async Task ResolveImageIndexAsync_ResolvesExactEditionIdFromDetailedImageMetadata(
        string edition,
        string editionId,
        int expectedIndex)
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "windows.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains($"/Index:{expectedIndex}", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = $"""
                        Index : {expectedIndex}
                        Name : Arbitrary localized Windows name
                        Edition : {editionId}
                        """
                }
                : arguments.Contains("/Index:", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                            Index : 1
                            Name : Windows Setup Media
                            """
                    }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = $"""
                        Index : 1
                        Name : Windows Setup Media

                        Index : {expectedIndex}
                        Name : Arbitrary localized Windows name
                        """
                    }
        };
        var service = new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance);

        int imageIndex = await service.ResolveImageIndexAsync(
            imagePath,
            edition,
            workspace.RootPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedIndex, imageIndex);
        Assert.Contains(processRunner.Calls, call => call.Contains($"/Index:{expectedIndex}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveImageIndexAsync_WhenEditionIdOccursMoreThanOnce_ThrowsWithoutFallback()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "windows.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = $"Index : {ParseRequestedIndex(arguments)}\nEdition : Professional" }
                : new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 8\nName : Pro first\n\nIndex : 9\nName : Pro second" }
        };
        var service = new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Pro",
                workspace.RootPath,
                TestContext.Current.CancellationToken));

        Assert.Contains("found 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyImageAsync_PreservesArgumentsProgressAndNativeTimeout(bool reportsProgress)
    {
        using var workspace = new TemporaryWorkspace();
        var runner = new RecordingProcessRunner { Result = new() { ExitCode = 0, StandardOutput = "[ 42.0% ]" } };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        var progress = new RecordingProgress();
        string image = Path.Combine(workspace.RootPath, "image with spaces.esd");
        string windows = Path.Combine(workspace.RootPath, "Windows partition");
        string scratch = Path.Combine(workspace.RootPath, "scratch");
        await service.ApplyImageAsync(image, 4, windows, scratch, workspace.RootPath,
            TestContext.Current.CancellationToken, reportsProgress ? progress : null);
        Assert.Equal(new[] { "/Apply-Image", $"/ImageFile:{image}", "/Index:4", $"/ApplyDir:{windows}", "/CheckIntegrity", $"/ScratchDir:{scratch}" }, runner.LastArgumentTokens);
        Assert.Equal(TimeSpan.FromHours(4), runner.LastTimeout);
        Assert.Equal(TestContext.Current.CancellationToken, runner.LastCancellationToken);
        Assert.Equal(reportsProgress ? new[] { 42d, 100d } : Array.Empty<double>(), progress.Values);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public async Task ApplyOfflineDriversAsync_PreservesArgumentsAndDoesNotReportSuccessAfterFailure(int exitCode)
    {
        using var workspace = new TemporaryWorkspace();
        var runner = new RecordingProcessRunner { Result = new() { ExitCode = exitCode, StandardOutput = "[ 42.0% ]" } };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        var progress = new RecordingProgress();
        string drivers = Path.Combine(workspace.RootPath, "driver folder");
        string windows = Path.Combine(workspace.RootPath, "Windows partition");
        string scratch = Path.Combine(workspace.RootPath, "scratch");
        Task operation = service.ApplyOfflineDriversAsync(windows, drivers, scratch, workspace.RootPath,
            TestContext.Current.CancellationToken, progress);
        if (exitCode == 0) await operation;
        else await Assert.ThrowsAsync<DeploymentProcessException>(() => operation);
        Assert.Equal(new[] { $"/Image:{windows}", "/Add-Driver", $"/Driver:{drivers}", "/Recurse", $"/ScratchDir:{scratch}" }, runner.LastArgumentTokens);
        Assert.Equal(exitCode == 0 ? new[] { 42d, 100d } : new[] { 42d }, progress.Values);
    }

    [Theory]
    [InlineData("Current Edition : Professional", "Professional")]
    [InlineData("No edition in this output", null)]
    public async Task GetAppliedWindowsEditionAsync_PreservesParsingAndMetadataTimeout(string output, string? expected)
    {
        var runner = new RecordingProcessRunner { Result = new() { ExitCode = 0, StandardOutput = output } };
        var service = new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance);
        Assert.Equal(expected, await service.GetAppliedWindowsEditionAsync("owned-fixture", Path.GetTempPath(), TestContext.Current.CancellationToken));
        Assert.Equal(TimeSpan.FromMinutes(2), runner.LastTimeout);
        Assert.Equal(new[] { "/English", "/Image:owned-fixture", "/Get-CurrentEdition" }, runner.LastArgumentTokens);
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private static int ParseRequestedIndex(string arguments)
    {
        string value = arguments[(arguments.LastIndexOf("/Index:", StringComparison.OrdinalIgnoreCase) + 7)..];
        return int.Parse(value);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public TimeSpan? LastTimeout { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public string? LastFileName { get; private set; }
        public string? LastArguments { get; private set; }
        public IReadOnlyList<string>? LastArgumentTokens { get; private set; }
        public string? LastWorkingDirectory { get; private set; }
        public ProcessExecutionResult Result { get; init; } = new() { ExitCode = 0 };
        public Func<string, ProcessExecutionResult>? ResultFactory { get; init; }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            Calls.Add($"{fileName} {arguments}");
            LastTimeout = executionTimeout;
            LastCancellationToken = cancellationToken;
            LastFileName = fileName;
            LastArguments = arguments;
            LastWorkingDirectory = workingDirectory;
            return Task.FromResult(ResultFactory?.Invoke(arguments) ?? Result);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            LastArgumentTokens = arguments.ToArray();
            string joinedArguments = string.Join(' ', LastArgumentTokens);
            Calls.Add($"{fileName} {joinedArguments}");
            LastTimeout = executionTimeout;
            LastCancellationToken = cancellationToken;
            LastFileName = fileName;
            LastArguments = joinedArguments;
            LastWorkingDirectory = workingDirectory;
            return Task.FromResult(ResultFactory?.Invoke(joinedArguments) ?? Result);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            LastArgumentTokens = arguments.ToArray();
            string joinedArguments = string.Join(' ', LastArgumentTokens);
            Calls.Add($"{fileName} {joinedArguments}");
            LastTimeout = executionTimeout;
            LastCancellationToken = cancellationToken;
            LastFileName = fileName;
            LastArguments = joinedArguments;
            LastWorkingDirectory = workingDirectory;
            ProcessExecutionResult result = ResultFactory?.Invoke(joinedArguments) ?? Result;
            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                onOutputData?.Invoke(result.StandardOutput);
            }

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                onErrorData?.Invoke(result.StandardError);
            }

            return Task.FromResult(result);
        }
    }
}

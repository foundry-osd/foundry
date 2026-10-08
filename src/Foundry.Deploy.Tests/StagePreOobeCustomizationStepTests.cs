// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using CoreConfiguration = Foundry.Core.Models.Configuration;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;
using NetworkProfileRoamingTransportSettings = Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings;

namespace Foundry.Deploy.Tests;

public sealed class StagePreOobeCustomizationStepTests
{
    [Fact]
    public async Task FailedCredentialRollbackRetainsNonRunnableOwnershipAndFailsStaging()
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, domain: true);
        string answer = Path.Combine(temp.WindowsRoot, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        File.WriteAllText(answer, "<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"><settings pass=\"specialize\"><component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"><ComputerName>LAB01</ComputerName></component></settings></unattend>");
        string root = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry");
        string credential = Path.Combine(root, "Payloads", "DomainJoin", context.RuntimeState.OperationId, "credentials.bin");
        FileStream? held = null;
        try
        {
            var service = new PreOobeTargetStagingService(path => Directory.CreateDirectory(path))
            {
                BeforeHookPublication = () =>
                {
                    held = new FileStream(credential, FileMode.Open, FileAccess.Read, FileShare.Read);
                    throw new IOException("injected failure");
                }
            };
            var result = await new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(), service).ExecuteAsync(context, TestContext.Current.CancellationToken);
            Assert.Equal(DeploymentStepState.Failed, result.State);
            Assert.True(File.Exists(credential));
            Assert.Null(context.DomainJoinInput);
            using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "State", "PreOobe", "plan.json")));
            Assert.Contains(plan.RootElement.GetProperty("ownedPayloads").EnumerateArray(), payload => payload.GetProperty("relativePath").GetString() == $"Payloads/DomainJoin/{context.RuntimeState.OperationId}/credentials.bin");
            using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "State", "PreOobe", "execution-result.json")));
            Assert.Equal("Staging", journal.RootElement.GetProperty("status").GetString());
            Assert.Null(context.RuntimeState.PreOobeManifestPath);
        }
        finally { held?.Dispose(); }
    }

    [Fact]
    public async Task UnsupportedActualEditionRetainsEvidenceAndFrozenRequestWithoutStaging()
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, domain: true);
        var input = context.DomainJoinInput!;
        var request = context.Request.DomainJoinRequest;
        await DomainJoinRuntimeEligibility.ConfirmEditionAsync(context, "Core", TestContext.Current.CancellationToken);
        await DomainJoinRuntimeEligibility.ConfirmEditionAsync(context, null, TestContext.Current.CancellationToken);
        Assert.Equal("Core", context.RuntimeState.ActualWindowsEditionId);
        Assert.Equal(DomainJoinExecutionStatus.SkippedUnsupportedEdition, context.RuntimeState.DomainJoinStatus);
        Assert.Equal(DomainJoinSkipCode.UnsupportedEdition, context.RuntimeState.DomainJoinSkipCode);
        Assert.Same(request, context.Request.DomainJoinRequest);
        Assert.NotNull(context.Request.DomainJoinIntent);
        Assert.Null(context.DomainJoinInput);
        Assert.Throws<ObjectDisposedException>(() => input.Password);
        Assert.DoesNotContain(DeploymentPlan.Build(context.Request, context.RuntimeState), entry => entry.Name == DeploymentStepNames.StagePreOobeCustomization);
        var result = await new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver()).ExecuteAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Null(context.RuntimeState.PreOobeManifestPath);
    }

    [Fact]
    public async Task FinalCompositionMismatchSkipsDomainWithoutCredentialOrInventedReport()
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, domain: true);
        var input = context.DomainJoinInput!;
        string answer = Path.Combine(temp.WindowsRoot, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        File.WriteAllText(answer, "<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"><settings pass=\"specialize\"><component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"><ComputerName>OTHER</ComputerName></component></settings></unattend>");
        var result = await new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(), new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)))
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal(DomainJoinExecutionStatus.SkippedImageComposition, context.RuntimeState.DomainJoinStatus);
        Assert.Equal(DomainJoinSkipCode.ComputerNameMismatch, context.RuntimeState.DomainJoinSkipCode);
        Assert.Null(context.DomainJoinInput);
        Assert.Throws<ObjectDisposedException>(() => input.Password);
        Assert.Null(context.RuntimeState.PreOobeManifestPath);
        Assert.False(Directory.Exists(Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "DomainJoin")));
        Assert.False(File.Exists(Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe", "domain-join-result.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DomainStagingPublishesBoundSeedsAndClearsInput(bool failPublication)
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, domain: true);
        var input = context.DomainJoinInput!;
        string answer = Path.Combine(temp.WindowsRoot, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        File.WriteAllText(answer, "<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"><settings pass=\"specialize\"><component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\"><ComputerName>LAB01</ComputerName></component></settings></unattend>");
        string credential = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "DomainJoin", context.RuntimeState.OperationId, "credentials.bin");
        bool protectedBeforeWrite = false;
        var service = new PreOobeTargetStagingService(path =>
        {
            Directory.CreateDirectory(path);
            if (path == Path.GetDirectoryName(credential))
            {
                protectedBeforeWrite = !File.Exists(credential);
            }
        })
        {
            BeforeHookPublication = () => { if (failPublication) throw new IOException("injected publication failure"); }
        };
        var result = await new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(), service).ExecuteAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(failPublication ? DeploymentStepState.Failed : DeploymentStepState.Succeeded, result.State);
        Assert.True(protectedBeforeWrite);
        Assert.Null(context.DomainJoinInput);
        Assert.Throws<ObjectDisposedException>(() => input.Password);
        Assert.Equal(!failPublication, File.Exists(credential));
        string stateRoot = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe");
        if (failPublication)
        {
            Assert.False(File.Exists(Path.Combine(stateRoot, "domain-join-phase.json")));
            Assert.False(File.Exists(Path.Combine(stateRoot, "domain-join-result.json")));
            return;
        }
        byte[] planBytes = File.ReadAllBytes(context.RuntimeState.PreOobeManifestPath!);
        using var plan = JsonDocument.Parse(planBytes);
        Assert.DoesNotContain("joiner", Encoding.UTF8.GetString(planBytes));
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(stateRoot, "execution-result.json")));
        Assert.Equal(new[] { "domain-join", "verify-domain-membership", "cleanup" }, plan.RootElement.GetProperty("actions").EnumerateArray().Select(action => action.GetProperty("id").GetString()));
        var payload = Assert.Single(plan.RootElement.GetProperty("ownedPayloads").EnumerateArray(), item => item.GetProperty("isSensitive").GetBoolean());
        Assert.Equal("domain-join", Assert.Single(payload.GetProperty("consumerActionIds").EnumerateArray()).GetString());
        using var decoded = Foundry.Core.Services.Configuration.DomainJoinCredentialPayloadCodec.Decode(File.ReadAllBytes(credential), new("example.com", "EXAMPLE\\joiner"));
        Assert.Equal("secret", new string(decoded.Password.Span));
        string hash = Convert.ToHexStringLower(SHA256.HashData(planBytes));
        using var phase = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stateRoot, "domain-join-phase.json")));
        Assert.Equal(hash, phase.RootElement.GetProperty("planHash").GetString());
        Assert.Equal(JsonValueKind.Null, phase.RootElement.GetProperty("originatingBootId").ValueKind);
        Assert.Equal("Prepared", phase.RootElement.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, phase.RootElement.GetProperty("computerObjectGuid").ValueKind);
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stateRoot, "domain-join-result.json")));
        Assert.Equal(hash, report.RootElement.GetProperty("planHash").GetString());
        Assert.Equal("", report.RootElement.GetProperty("originatingBootId").GetString());
    }

    [Fact]
    public async Task MissingBootstrapRuntime_IsRecoveredForBuiltInOemActivation()
    {
        using var temp = new TemporaryDirectory();
        using var recovery = new PostInstallRuntimeRecoveryTests.Fixture();
        using var context = CreateContext(temp, licenseChannel: "RET");
        var resolver = new PreOobeContentResolver { RuntimeExecutablePath = null, RuntimeRecovery = recovery.Recovery };
        using var prepared = await resolver.PrepareAsync(context, TestContext.Current.CancellationToken);
        Assert.NotNull(prepared);
        Assert.Equal(2, recovery.Requests.Count);
        Assert.True(File.Exists(Path.Combine(prepared.RuntimeDirectory, "Foundry.PostInstall.exe")));
        Assert.False(File.Exists(recovery.CacheArchive));
    }

    [Fact]
    public async Task NoPostInstallTasks_DoesNotResolveRuntime()
    {
        using var temp = new TemporaryDirectory();
        using var recovery = new PostInstallRuntimeRecoveryTests.Fixture();
        using var context = CreateContext(temp);
        var resolver = new PreOobeContentResolver { RuntimeExecutablePath = null, RuntimeRecovery = recovery.Recovery };
        Assert.Null(await resolver.PrepareAsync(context, TestContext.Current.CancellationToken));
        Assert.Empty(recovery.Requests);
    }

    [Theory]
    [InlineData("builtin_task")]
    [InlineData("inline_command")]
    [InlineData("disabled_package")]
    [InlineData("malformed_binding")]
    public async Task BoundMediaGeneration_IsNotConsultedWithoutPackageActions(string scenario)
    {
        using var temp = new TemporaryDirectory();
        CoreConfiguration.PreOobeActionSettings[] actions = scenario switch
        {
            "inline_command" => [CoreConfiguration.PreOobeActionSettings.Create(CoreConfiguration.PreOobeActionKind.Command, "Inline") with { Command = "cmd.exe /c exit 0" }],
            "disabled_package" => [CreatePackageAction() with { IsEnabled = false }],
            _ => []
        };
        using var context = CreateContext(temp, licenseChannel: scenario == "inline_command" ? "" : "RET", postInstall: new()
        {
            IsEnabled = actions.Length != 0,
            Actions = actions,
            ManifestId = scenario == "malformed_binding" ? "not-a-guid" : Guid.NewGuid().ToString("N"),
            ManifestHash = scenario == "malformed_binding" ? "damaged" : new string('a', 64)
        });
        var resolver = new PreOobeContentResolver
        {
            RuntimeExecutablePath = NativeRuntimeFixture.CreateFiles(temp.RootPath),
            MediaRoots = () => throw new InvalidOperationException("External media must not be enumerated without package actions.")
        };

        using var prepared = await resolver.PrepareAsync(context, TestContext.Current.CancellationToken);

        Assert.NotNull(prepared);
        Assert.Empty(prepared.Packages);
    }

    [Theory]
    [InlineData(true, "The referenced post-installation media generation is unavailable.")]
    [InlineData(false, "Package actions require an authenticated external media manifest.")]
    public async Task MediaGeneration_RemainsRequiredForPackageActions(bool bound, string expectedMessage)
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, postInstall: new()
        {
            IsEnabled = true,
            Actions = [CreatePackageAction()],
            ManifestId = bound ? Guid.NewGuid().ToString("N") : null,
            ManifestHash = bound ? new string('a', 64) : null
        });
        var resolver = new PreOobeContentResolver { RuntimeExecutablePath = NativeRuntimeFixture.CreateFiles(temp.RootPath), MediaRoots = () => [] };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => resolver.PrepareAsync(context, TestContext.Current.CancellationToken));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task MediaPublisher_PackagesAreCombinedWithBootstrapRuntimeAndRemainLocked()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.RootPath, "hello.ps1");
        await File.WriteAllTextAsync(source, "Write-Output 'fixture'", TestContext.Current.CancellationToken);
        var library = new Foundry.Core.Services.Packages.PreOobePackageLibraryService(Path.Combine(temp.RootPath, "library"));
        var reference = await library.ImportAsync(source, TestContext.Current.CancellationToken);
        var settings = new Foundry.Core.Models.Configuration.PreOobeSettings
        {
            IsEnabled = true,
            Actions = [new() { Id = Guid.NewGuid().ToString("N"), Name = "Fixture", Kind = Foundry.Core.Models.Configuration.PreOobeActionKind.PowerShell,
                Package = reference, EntryPoint = "hello.ps1",
                Process = new() }]
        };
        using var runtime = NativeRuntimeFixture.Create(temp.RootPath);
        using var recovery = new PostInstallRuntimeRecoveryTests.Fixture();
        var publisher = new Foundry.Core.Services.WinPe.WinPePreOobeMediaService();
        using var media = await publisher.PrepareAsync(library, settings, TestContext.Current.CancellationToken);
        string mediaRoot = Path.Combine(temp.RootPath, "media");
        Directory.CreateDirectory(mediaRoot);
        await publisher.PublishAsync(media, mediaRoot, TestContext.Current.CancellationToken);
        using DeploymentStepExecutionContext context = CreateContext(temp, postInstall: new()
        { IsEnabled = true, Actions = settings.Actions, ManifestId = media.ManifestId, ManifestHash = media.ManifestHash });
        var resolver = new PreOobeContentResolver { RuntimeExecutablePath = Path.Combine(runtime.RuntimeDirectory, "Foundry.PostInstall.exe"), RuntimeRecovery = recovery.Recovery, MediaRoots = () => [mediaRoot] };
        using var prepared = await resolver.PrepareAsync(context, TestContext.Current.CancellationToken);
        Assert.NotNull(prepared);
        Assert.Equal(reference.ContentHash, Assert.Single(prepared.Packages).ContentHash);
        Assert.Equal(runtime.RuntimeDirectory, prepared.RuntimeDirectory);
        Assert.Empty(recovery.Requests);
        Assert.Throws<IOException>(() => File.Delete(Path.Combine(prepared.Packages[0].SourceRoot, "hello.ps1")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedHookPublication_RemovesOnlyNewInputsAndRunnableState(bool customAnswer, bool cancelled)
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, usesCustomUnattend: customAnswer);
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();
        string answerPath = Path.Combine(temp.WindowsRoot, "Windows", "Panther", "unattend.xml");
        byte[]? originalAnswer = null;
        if (customAnswer)
        {
            originalAnswer = new Foundry.Deploy.Services.Deployment.Unattend.PreOobeUnattendHookService()
                .Prepare(System.Text.Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "x64");
            Directory.CreateDirectory(Path.GetDirectoryName(answerPath)!);
            await File.WriteAllBytesAsync(answerPath, originalAnswer, TestContext.Current.CancellationToken);
        }
        string preserved = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "NetworkProfiles", "existing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
        File.WriteAllText(preserved, "preserve");
        var service = new PreOobeTargetStagingService(path => Directory.CreateDirectory(path))
        {
            BeforeHookPublication = () =>
            {
                string journalPath = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe", "execution-result.json");
                using var journal = JsonDocument.Parse(File.ReadAllText(journalPath));
                Assert.Equal("Staging", journal.RootElement.GetProperty("status").GetString());
                if (cancelled) throw new OperationCanceledException();
                throw new IOException("injected publication failure");
            }
        };
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(), service);
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => step.ExecuteAsync(context, TestContext.Current.CancellationToken));
        else Assert.Equal(DeploymentStepState.Failed, (await step.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.True(File.Exists(preserved));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(preserved)!, "wifi-profile.xml")));
        string stateRoot = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe");
        Assert.False(File.Exists(Path.Combine(stateRoot, "plan.json")));
        Assert.False(File.Exists(Path.Combine(stateRoot, "execution-result.json")));
        if (customAnswer) Assert.Equal(originalAnswer, File.ReadAllBytes(answerPath));
        else Assert.False(File.Exists(answerPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageDriverInstaller_StagesOnlyPayloadWithoutSetupHook(bool dryRun)
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory, isDryRun: dryRun);
        string source = Path.Combine(tempDirectory.RootPath, "driver.exe");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = source;

        DeploymentStepResult result = await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.Equal(!dryRun, File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        if (!dryRun)
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(context.RuntimeState.DeferredDriverPackagePath!, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverInstallerWasStaged_DoesNotRequireOrCopyOriginalDownload()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        string stagedPath = Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllBytesAsync(stagedPath, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DeferredDriverPackagePath = stagedPath;
        context.RuntimeState.DownloadedDriverPackPath = Path.Combine(tempDirectory.RootPath, "missing-download.exe");
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(stagedPath, TestContext.Current.CancellationToken));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(context.RuntimeState.PreOobeManifestPath!, TestContext.Current.CancellationToken));
        Assert.Equal(context.RuntimeState.OperationId, manifest.RootElement.GetProperty("operationId").GetString());
        JsonElement driver = manifest.RootElement.GetProperty("actions").EnumerateArray().Single(script => script.GetProperty("id").GetString() == "driver-pack");
        Assert.Equal("Payloads/Drivers/driver.exe", driver.GetProperty("parameters").GetProperty("packagePath").GetString());
    }

    [Theory]
    [InlineData("RET", false, false, true)]
    [InlineData("ret", false, true, true)]
    [InlineData("VOL", false, false, false)]
    [InlineData("RET", true, false, false)]
    [InlineData("RET", true, true, false)]
    [InlineData("", false, false, false)]
    [InlineData("OEM", false, false, false)]
    public async Task StagePreOobeCustomizationStep_OnlyStandardRetailDeploymentsStageOemActivation(
        string licenseChannel,
        bool usesCustomUnattend,
        bool isDryRun,
        bool expectsActivation)
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel, usesCustomUnattend, isDryRun);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expectsActivation ? DeploymentStepState.Succeeded : DeploymentStepState.Skipped, result.State);
        if (expectsActivation && !isDryRun)
        {
            using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
            Assert.Contains(plan.RootElement.GetProperty("actions").EnumerateArray(), action => action.GetProperty("id").GetString() == "windows-oem-activation");
            string stagedRuntime = Path.GetDirectoryName(context.RuntimeState.PreOobeRunnerPath)!;
            foreach (var file in context.PostInstallContent!.RuntimeManifest.Files)
            {
                Assert.Equal(File.ReadAllBytes(Path.Combine(context.PostInstallContent.RuntimeDirectory, file.RelativePath)),
                    File.ReadAllBytes(Path.Combine(stagedRuntime, file.RelativePath)));
            }
        }
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRoamingPayloadExists_StagesImporterAndCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.True(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "NetworkProfiles", "wifi-profile.xml")));
        Assert.Contains("network-profile-roaming", File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Contains("OnRequest", File.ReadAllText(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Panther", "unattend.xml")));
        using JsonDocument journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(context.RuntimeState.PreOobeManifestPath!)!, "execution-result.json")));
        Assert.Equal("Pending", journal.RootElement.GetProperty("status").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(context.RuntimeState.PreOobeManifestPath!))).ToLowerInvariant(), journal.RootElement.GetProperty("planHash").GetString());
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRetailWithDriversAndRoaming_StagesActivationBeforeCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel: "RET");
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.True(File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Equal(new[] { "driver-pack", "network-profile-roaming", "windows-oem-activation", "cleanup" },
            plan.RootElement.GetProperty("actions").EnumerateArray().Select(action => action.GetProperty("id").GetString()));
        JsonElement network = plan.RootElement.GetProperty("ownedPayloads").EnumerateArray().Single(payload => payload.GetProperty("relativePath").GetString() == "Payloads/NetworkProfiles");
        Assert.Equal("network-profile-roaming", Assert.Single(network.GetProperty("consumerActionIds").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenDeferredDriverPayloadIsMissing_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
    }

    [Fact]
    public async Task StageDriverInstaller_WhenDeferredDriverCommandIsUnsupported_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        var step = new StageDriverInstallerStep(new FakeDriverPackStrategyResolver(DeferredDriverPackageCommandKind.None));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
    }

    private static PreOobeNetworkProfileRoamingPayload CreateRoamingPayload()
    {
        return new PreOobeNetworkProfileRoamingPayload
        {
            DataFiles =
            [
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "wifi-profile.xml"),
                    Content = "<WLANProfile />"
                },
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "import-settings.json"),
                    Content = "{}"
                }
            ]
        };
    }

    private static CoreConfiguration.PreOobeActionSettings CreatePackageAction() => new()
    {
        Name = "Fixture",
        Kind = CoreConfiguration.PreOobeActionKind.PowerShell,
        Package = new() { ContentHash = new string('b', 64), DisplayName = "Fixture", Length = 1, FileCount = 1 },
        EntryPoint = "script.ps1",
        Process = new()
    };

    private static DeploymentStepExecutionContext CreateContext(
        TemporaryDirectory tempDirectory,
        string licenseChannel = "",
        bool usesCustomUnattend = false,
        bool isDryRun = false,
        Foundry.Core.Models.Configuration.Deploy.DeployPreOobeSettings? postInstall = null,
        bool domain = false)
    {
        var request = new DeploymentContext
        {
            PreOobe = postInstall ?? new(),
            DomainJoinRequest = domain ? new(Services.DomainJoin.DomainJoinDeploymentDisposition.Ready) : null,
            DomainJoinIntent = domain ? new("example.com", "LAB01", null) : null,
            Mode = DeploymentMode.Iso,
            IsDryRun = isDryRun,
            Unattend = usesCustomUnattend
                ? new UnattendSelection(new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile(), "custom.xml")
                : null,
            CacheRootPath = tempDirectory.WorkspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = licenseChannel, Architecture = "x64" },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem()
        };
        var runtimeState = new DeploymentRuntimeState
        {
            OperationId = Guid.NewGuid().ToString("N"),
            WorkspaceRoot = tempDirectory.WorkspaceRoot,
            Mode = DeploymentMode.Iso,
            TargetWindowsPartitionRoot = tempDirectory.WindowsRoot,
            TargetFoundryRoot = tempDirectory.TargetFoundryRoot,
            ResolvedCache = new CacheResolution
            {
                RootPath = tempDirectory.WorkspaceRoot,
                Source = "test"
            },
            Network = new CoreDeployNetworkSettings
            {
                ProfileRoaming = new CoreDeployNetworkProfileRoamingSettings
                {
                    WiredDot1x = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
                    Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
                    ArtifactRootPath = tempDirectory.RootPath
                }
            }
        };

        DomainJoinRuntimeEligibility.Initialize(request, runtimeState);
        var context = new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            _ => { }, domainJoinInput: domain ? new(new("example.com", "EXAMPLE\\joiner"), "LAB01", null, "secret") : null);
        string fixtureRuntime = Path.Combine(tempDirectory.RootPath, "context-runtime");
        Directory.CreateDirectory(fixtureRuntime);
        context.PostInstallContent = NativeRuntimeFixture.Create(fixtureRuntime);
        return context;
    }

    private sealed class FakeDriverPackStrategyResolver(
        DeferredDriverPackageCommandKind commandKind = DeferredDriverPackageCommandKind.LenovoExecutable) : IDriverPackStrategyResolver
    {
        public DriverPackExecutionPlan Resolve(
            DriverPackSelectionKind selectionKind,
            DriverPackCatalogItem? driverPack,
            string downloadedPath)
        {
            return new DriverPackExecutionPlan
            {
                InstallMode = DriverPackInstallMode.DeferredSetupComplete,
                ExtractionMethod = DriverPackExtractionMethod.None,
                DeferredCommandKind = commandKind,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = ".exe",
                Manufacturer = "Lenovo",
            };
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            string logsDirectory = Path.Combine(rootPath, "Logs");
            string stateDirectory = Path.Combine(rootPath, "State");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(stateDirectory);

            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = logsDirectory,
                StateDirectoryPath = stateDirectory,
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
            };
        }

        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

    }

    private sealed class FakeOperationProgressService : IOperationProgressService
    {
        public bool IsOperationInProgress => false;
        public int Progress => 0;
        public string? Status => null;
        public OperationKind? CurrentOperation => null;
        public bool CanStartOperation => true;
        public event EventHandler? ProgressChanged;
        public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0) => true;
        public void Report(int progress, string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Complete(string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Fail(string status) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void ResetToIdle() => ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(0);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
            WorkspaceRoot = Path.Combine(RootPath, "Workspace");
            WindowsRoot = Path.Combine(RootPath, "Windows");
            TargetFoundryRoot = Path.Combine(WindowsRoot, "Foundry");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(WindowsRoot);
            Directory.CreateDirectory(TargetFoundryRoot);
        }

        public string RootPath { get; }

        public string WorkspaceRoot { get; }

        public string WindowsRoot { get; }

        public string TargetFoundryRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

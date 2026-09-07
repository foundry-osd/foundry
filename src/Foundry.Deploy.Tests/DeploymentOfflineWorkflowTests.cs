// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Startup;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentOfflineWorkflowTests
{
    [Fact]
    public void ConfigurationSnapshot_BindsParsedSettingsAndDigestToCapturedBytes()
    {
        byte[] input = Encoding.UTF8.GetBytes("{\"operatingSystemSelection\":{\"isEnabled\":true,\"defaultReleaseId\":\"24H2\"}}");
        string expected = Convert.ToHexString(SHA256.HashData(input));
        DeploymentOfflineConfigurationSnapshot snapshot = DeploymentOfflineConfigurationSnapshot.Parse(input);
        Array.Fill(input, (byte)'x');
        byte[] returnedCopy = snapshot.CopyContent();
        Array.Fill(returnedCopy, (byte)'y');
        Assert.Equal("24H2", snapshot.Document.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(expected, snapshot.Digest);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(snapshot.CopyContent())));
    }

    [Fact]
    public async Task ConfigurationRead_RejectsOversizedFileBeforeParsing()
    {
        string path = Path.Combine(Path.GetTempPath(), "foundry-readiness-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var file = File.Create(path)) file.SetLength(4 * 1024 * 1024 + 1);
            await Assert.ThrowsAsync<InvalidDataException>(() => DeploymentOfflineWorkflow.ReadConfigurationAsync(path, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCustomization_ProtectedMediaRequiresActualUnlockedSession(bool unlocked)
    {
        using var session = new DeploymentSecretKeySession();
        if (unlocked) session.SetKey(new byte[32]);
        var workflow = new DeploymentOfflineWorkflow(new NoDownloads(), secretSession: session);
        var document = new FoundryDeployConfigurationDocument { Protection = new() { IsEnabled = true } };
        IReadOnlyList<string> blockers = await workflow.ValidateCurrentCustomizationAsync(Request(), document, TestContext.Current.CancellationToken);
        Assert.Equal(!unlocked, blockers.Count > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCustomization_ValidatesActualSelectedProfileAndClearsReturnedBytes(bool invalid)
    {
        byte[] content = invalid ? Encoding.UTF8.GetBytes("{}") : Encoding.ASCII.GetBytes(AutopilotOfflineProfileConverter.Convert(new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Synthetic",
            Mode = AutopilotOfflineDeploymentMode.UserDriven,
            JoinType = AutopilotOfflineJoinType.Entra,
            UserType = AutopilotOfflineUserType.Administrator
        }, new(Guid.NewGuid(), "example.onmicrosoft.com")));
        var selected = new AutopilotProfileCatalogItem { DisplayName = "Chosen", FolderName = "Chosen", ConfigurationFilePath = "selected.json" };
        var profiles = new Profiles(content);
        var workflow = new DeploymentOfflineWorkflow(new NoDownloads(), profiles);
        var document = new FoundryDeployConfigurationDocument
        { Autopilot = new() { IsEnabled = true, DefaultProfileFolderName = "DifferentDefault" } };
        IReadOnlyList<string> blockers = await workflow.ValidateCurrentCustomizationAsync(Request() with
        { IsAutopilotEnabled = true, SelectedAutopilotProfile = selected }, document, TestContext.Current.CancellationToken);
        Assert.Same(selected, profiles.Requested);
        Assert.Equal(invalid, blockers.Count > 0);
        Assert.All(content, value => Assert.Equal((byte)0, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCustomization_ValidatesSelectedCustomAnswerFileEvenWithoutConfiguredDefault(bool corrupt)
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-offline-answer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var session = new DeploymentSecretKeySession();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        session.SetKey(key);
        try
        {
            byte[] xml = Encoding.UTF8.GetBytes("<unattend xmlns='urn:schemas-microsoft-com:unattend'><settings pass='specialize'><component name='Microsoft-Windows-Shell-Setup' processorArchitecture='amd64'><ComputerName>SYNTHETIC</ComputerName></component></settings></unattend>");
            var file = new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile
            { Id = Guid.NewGuid().ToString("N"), DisplayName = "Chosen", ContentHash = Convert.ToHexString(SHA256.HashData(xml)) };
            string path = Path.Combine(root, UnattendFileService.GetAssetFileName(file.Id));
            string json = JsonSerializer.Serialize(MediaSecretEnvelopeProtector.EncryptBytes(xml, key, MediaSecretEnvelopeProtector.DeploymentKeyId));
            await File.WriteAllTextAsync(path, corrupt ? "{}" : json, TestContext.Current.CancellationToken);
            var workflow = new DeploymentOfflineWorkflow(new NoDownloads(), secretSession: session, unattendContent: new(session));
            IReadOnlyList<string> blockers = await workflow.ValidateCurrentCustomizationAsync(Request() with
            { Unattend = new(file, path) }, new(), TestContext.Current.CancellationToken);
            Assert.Equal(corrupt, blockers.Count > 0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DriverRequirement_UsesExecutionManufacturerCacheDirectory()
    {
        var driver = new DriverPackCatalogItem
        {
            Manufacturer = "Dell",
            Id = "synthetic",
            PackageId = "synthetic",
            CatalogRevision = "sha256:" + new string('b', 64),
            FileName = "drivers.zip",
            DownloadUrl = "https://example.com/drivers.zip",
            Sha256 = new string('a', 64),
            SizeBytes = 3
        };
        OfflineArtifactRequirement requirement = DeploymentOfflineWorkflow.DriverRequirement(driver, @"C:\CacheRoot");
        string directory = Path.Combine(@"C:\CacheRoot", "Cache", "DriverPacks", DeploymentStepExecutionContext.SanitizePathSegment(driver.Manufacturer));
        Assert.Equal(new[] { Path.Combine(directory, requirement.Identity.CacheKey, driver.FileName), Path.Combine(directory, driver.FileName) }, requirement.CandidatePaths);
    }

    [Fact]
    public async Task ReadinessCommand_FutureConfigurationReturnsInvalidWithoutStartingRuntime()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-readiness-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string configuration = Path.Combine(root, "configuration.json");
            string output = Path.Combine(root, "result.json");
            await File.WriteAllTextAsync(configuration, "{\"schemaVersion\":999}", TestContext.Current.CancellationToken);
            int exitCode = await DeploymentOfflineReadinessCommand.RunAsync(
                ["--check-offline-readiness", "--config", configuration, "--result", output, "--nonce", Guid.NewGuid().ToString("D")]);
            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReadinessCommand_MalformedNonceReturnsInvalidBeforeReadingConfiguration()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-readiness-nonce-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(root, "result.json");
        int exitCode = await DeploymentOfflineReadinessCommand.RunAsync(
            ["--check-offline-readiness", "--config", Path.Combine(root, "missing.json"),
             "--result", output, "--nonce", "not-a-guid"]);
        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(root));
    }

    private static DeploymentContext Request() => new()
    {
        Mode = DeploymentMode.Iso,
        CacheRootPath = "unused",
        TargetDiskNumber = 1,
        TargetComputerName = "SYNTHETIC",
        OperatingSystem = new() { Architecture = "x64" },
        DriverPackSelectionKind = DriverPackSelectionKind.None,
        ApplyFirmwareUpdates = false
    };

    private sealed class Profiles(byte[] content) : IAutopilotProfileContentService
    {
        public AutopilotProfileCatalogItem? Requested { get; private set; }
        public Task<byte[]> ReadAsync(AutopilotProfileCatalogItem profile, CancellationToken cancellationToken = default)
        { Requested = profile; return Task.FromResult(content); }
    }
    private sealed class NoDownloads : IArtifactDownloadService
    {
        public Task<ArtifactDownloadResult?> TryUseCachedAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Local customization validation must not acquire artifacts.");
        public Task<ArtifactDownloadResult> DownloadAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
            => throw new InvalidOperationException("Offline validation must not access the network.");
    }
}

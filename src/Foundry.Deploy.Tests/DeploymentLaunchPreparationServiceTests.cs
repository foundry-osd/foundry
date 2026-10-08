// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Deployment;
using System.Globalization;
using System.Text.Json;
using Foundry.Utilities.Storage;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Security;
using DeployDomainJoinSettings = Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentLaunchPreparationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOuSelectionStopsBeforeDestructiveConfirmation(bool automatic)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        var shell = new FakeApplicationShellService();
        // Two listed OUs make the technician choose; a single one would be used without a selection.
        DeployDomainJoinSettings settings = DomainJoinPreparationServiceTests.WithTwoOus(DomainJoinPreparationServiceTests.WithOneOu(automatic
            ? DomainJoinPreparationServiceTests.Automatic(new("corp.test", "CORP\\join"), key) : new() { IsEnabled = true }));
        using DomainJoinSubmission submission = Submission();
        DeploymentLaunchPreparationService service = CreateService(shell, keys);

        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), settings, submission);

        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
        Assert.Null(result.TakeDomainJoinInput());
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Theory]
    [InlineData("OU=Field,DC=corp,DC=test", true)]
    [InlineData("OU=Field,DC=other,DC=test", false)]
    public void TypedOuIsValidatedBeforeDestructiveConfirmation(string typedOu, bool valid)
    {
        using var keys = new DeploymentSecretKeySession();
        var shell = new FakeApplicationShellService();
        using DomainJoinSubmission submission = Submission(typedOu: typedOu);
        DeploymentLaunchPreparationService service = CreateService(shell, keys);

        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), new() { IsEnabled = true }, submission);

        Assert.Equal(valid, result.IsReadyToStart);
        Assert.Equal(valid ? 1 : 0, shell.ConfirmationCallCount);
        if (valid)
        {
            Assert.Equal(typedOu, result.Context!.DomainJoinIntent!.TargetOuDn);
            Assert.DoesNotContain("CORP", JsonSerializer.Serialize(result.Context));
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(result.Context));
        }
        else
        {
            Assert.Null(result.Context);
            Assert.Null(result.TakeDomainJoinInput());
        }
    }

    [Fact]
    public void InteractiveInputReachesTheContextWithoutCredentials()
    {
        using var keys = new DeploymentSecretKeySession();
        var shell = new FakeApplicationShellService();
        using DomainJoinSubmission submission = Submission();
        DeploymentLaunchPreparationService service = CreateService(shell, keys);
        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), new() { IsEnabled = true }, submission);
        Assert.True(result.IsReadyToStart);
        Assert.Equal(1, shell.ConfirmationCallCount);
        Assert.Equal(new DomainJoinDeploymentIntent("corp.test", "LAB-01", null), result.Context!.DomainJoinIntent);
        Assert.Equal(DomainJoinDeploymentDisposition.Ready, result.Context.DomainJoinRequest!.Disposition);
        // The erase confirmation is about the disk and the image; the join is reviewed on the summary page.
        Assert.DoesNotContain("corp.test", shell.LastConfirmationMessage);
        string json = JsonSerializer.Serialize(result.Context);
        Assert.DoesNotContain("CORP", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("encryptedCredentials", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CORP", result.Context.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingWizardInputNeverErasesDisk(bool automatic)
    {
        using var keys = new DeploymentSecretKeySession();
        byte[] key = new byte[32];
        keys.SetKey(key);
        var shell = new FakeApplicationShellService();
        DeploymentLaunchPreparationService service = CreateService(shell, keys);
        // Zero-touch only needs wizard input when the technician has to choose, here between two OUs.
        DeployDomainJoinSettings settings = automatic
            ? DomainJoinPreparationServiceTests.WithTwoOus(DomainJoinPreparationServiceTests.WithOneOu(DomainJoinPreparationServiceTests.Automatic(new("corp.test", "CORP\\join"), key)))
            : new() { IsEnabled = true };
        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), settings, domainJoinSubmission: null);
        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
        Assert.Equal(0, shell.ConfirmationCallCount);
        Assert.Null(result.TakeDomainJoinInput());
    }

    [Fact]
    public void DeclinedConfirmationDisposesInput()
    {
        var input = new DomainJoinPreparedInput(new("corp.test", "CORP\\join"), "LAB-01", null, "secret".AsSpan());
        ReadOnlyMemory<char> password = input.Password;
        var shell = new FakeApplicationShellService { ConfirmationResult = false };
        var service = new DeploymentLaunchPreparationService(shell,
            domainJoinPreparationService: new OwnedInputPreparation(input));
        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), new() { IsEnabled = true });
        Assert.False(result.IsReadyToStart);
        Assert.Null(result.TakeDomainJoinInput());
        Assert.All(password.ToArray(), value => Assert.Equal('\0', value));
    }

    private sealed class OwnedInputPreparation(DomainJoinPreparedInput input) : IDomainJoinPreparationService
    {
        public DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission) =>
            DomainJoinPreparationResult.Ready(input);
    }

    /// <summary>Fails the test when credentials are prepared on a path that must not touch them.</summary>
    private sealed class ForbiddenPreparation : IDomainJoinPreparationService
    {
        public DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission) =>
            throw new InvalidOperationException("Credentials must not be prepared.");
    }

    private static DomainJoinSubmission Submission(string? selectedOuId = null, string? typedOu = null) =>
        new(null, "corp.test", "CORP\\join", selectedOuId, "secret".AsSpan(), typedOu);

    private static DeploymentLaunchPreparationService CreateService(FakeApplicationShellService shell, DeploymentSecretKeySession keys) =>
        new(shell, domainJoinPreparationService: new DomainJoinPreparationService(keys));

    [Fact]
    public void DisposeClearsUntransferredInput()
    {
        var input = new DomainJoinPreparedInput(new("corp.test", "CORP\\join"), "LAB-01", null, "secret".AsSpan());
        ReadOnlyMemory<char> password = input.Password;
        var service = new DeploymentLaunchPreparationService(new FakeApplicationShellService(),
            domainJoinPreparationService: new OwnedInputPreparation(input));
        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(CreateDisk()), new() { IsEnabled = true });
        Assert.True(result.IsReadyToStart);
        result.Dispose();
        Assert.Null(result.TakeDomainJoinInput());
        Assert.All(password.ToArray(), value => Assert.Equal('\0', value));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Core")]
    [InlineData("Home N")]
    [InlineData("CoreSingleLanguage")]
    public void KnownUnsupportedEditionNeverPreparesCredentials(string edition)
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell, domainJoinPreparationService: new ForbiddenPreparation());
        DeploymentLaunchRequest request = CreateRequest(CreateDisk());
        request = request with { SelectedOperatingSystem = (OperatingSystemCatalogItem)request.SelectedOperatingSystem! with { Edition = edition } };
        using DomainJoinSubmission submission = Submission();
        using DeploymentLaunchPreparationResult result = service.Prepare(request, new() { IsEnabled = true }, submission);
        Assert.True(result.IsReadyToStart);
        Assert.Null(result.TakeDomainJoinInput());
        Assert.Null(result.Context!.DomainJoinIntent);
        Assert.Equal(DomainJoinDeploymentDisposition.UnsupportedEdition, result.Context.DomainJoinRequest!.Disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DryRunSimulatesTheJoinTargetWithoutPreparingCredentials(bool hasWizardInput)
    {
        var service = new DeploymentLaunchPreparationService(new FakeApplicationShellService(), domainJoinPreparationService: new ForbiddenPreparation());
        using DomainJoinSubmission? submission = hasWizardInput ? Submission(typedOu: "OU=Field,DC=corp,DC=test") : null;
        using DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(null, isDryRun: true), new() { IsEnabled = true }, submission);
        Assert.True(result.IsReadyToStart);
        Assert.Null(result.TakeDomainJoinInput());
        Assert.Equal(hasWizardInput ? new DomainJoinDeploymentIntent("corp.test", "LAB-01", "OU=Field,DC=corp,DC=test") : null,
            result.Context!.DomainJoinIntent);
        Assert.Equal(DomainJoinDeploymentDisposition.DryRun, result.Context.DomainJoinRequest!.Disposition);
    }

    [Fact]
    public void TransferredInputSurvivesResultDisposal()
    {
        using var keys = new DeploymentSecretKeySession();
        using DomainJoinSubmission submission = Submission();
        DeploymentLaunchPreparationService service = CreateService(new FakeApplicationShellService(), keys);
        var result = service.Prepare(CreateRequest(CreateDisk()), new() { IsEnabled = true }, submission);
        using DomainJoinPreparedInput? input = result.TakeDomainJoinInput();
        ReadOnlyMemory<char> password = input!.Password;
        result.Dispose();
        Assert.Null(result.TakeDomainJoinInput());
        Assert.Equal("secret", new string(password.Span));
        input.Dispose();
        Assert.All(password.ToArray(), value => Assert.Equal('\0', value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_PreservesNameUploadPreferenceWithFinalNormalizedName(bool upload)
    {
        var service = new DeploymentLaunchPreparationService(new FakeApplicationShellService());
        var request = CreateRequest(CreateDisk(), targetComputerName: " Edited-042 ") with
        {
            UploadComputerNameToAutopilot = upload
        };

        DeploymentLaunchPreparationResult result = service.Prepare(request);

        Assert.True(result.IsReadyToStart);
        Assert.Equal("Edited-042", result.Context!.TargetComputerName);
        Assert.Equal(upload, result.Context.UploadComputerNameToAutopilot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_WhenIdentityIsMissingOrUnusable_FailsBeforeConfirmation(bool missing)
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo disk = CreateDisk() with
        {
            Identity = missing ? null : new DiskIdentity(3, "", "", "NVMe Disk", "NVMe", 4096)
        };

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(disk));

        Assert.False(result.IsReadyToStart);
        Assert.Equal(0, shell.ConfirmationCallCount);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public void Prepare_RetainsRawConfirmedIdentityWithoutPersistingIt()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo disk = CreateDisk() with { SerialNumber = "Localized display only" };

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(disk));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(1, shell.ConfirmationCallCount);
        Assert.Equal("SERIAL-3", result.Context!.TargetDiskIdentity!.SerialNumber);
        Assert.Equal(256UL * 1024 * 1024 * 1024, result.Context.TargetDiskIdentity.SizeBytes);
        Assert.DoesNotContain("SERIAL-3", JsonSerializer.Serialize(result.Context), StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL-3", JsonSerializer.Serialize(disk), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123456789012345")]
    [InlineData(" 123_456 ")]
    public void Prepare_WhenComputerNameContainsOnlyDigits_FailsBeforeConfirmation(string computerName)
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(selectedTargetDisk: CreateDisk(), targetComputerName: computerName));

        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenDryRunAndTargetDiskMissing_UsesDebugVirtualDisk()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(selectedTargetDisk: null, isDryRun: true));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(999, result.EffectiveTargetDisk?.DiskNumber);
        Assert.Equal("LAB-01", result.NormalizedComputerName);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenSelectedDiskIsBlocked_FailsBeforeConfirmation()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo blockedDisk = CreateDisk(isSelectable: false, selectionWarning: "System disk");

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(selectedTargetDisk: blockedDisk));

        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenOemDriverPackSelectionHasNoPackage_FailsValidation()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                driverPackSelectionKind: DriverPackSelectionKind.OemCatalog,
                selectedDriverPack: null));

        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
    }

    [Fact]
    public void Prepare_WhenRequestIsValidAndConfirmed_ReturnsDeploymentContext()
    {
        var shell = new FakeApplicationShellService { ConfirmationResult = true };
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo targetDisk = CreateDisk();
        DriverPackCatalogItem driverPack = new()
        {
            Id = "pack-1",
            Manufacturer = "Dell",
            Name = "Dell 24H2",
            FileName = "pack.cab",
            DownloadUrl = "https://example.test/pack.cab",
            OsName = "Windows 11",
            OsReleaseId = "24H2",
            OsArchitecture = "x64"
        };
        AutopilotProfileCatalogItem autopilotProfile = new()
        {
            FolderName = "profile",
            DisplayName = "Corporate Profile",
            ConfigurationFilePath = @"C:\Autopilot\profile.json"
        };
        DeployOobeSettings oobe = new()
        {
            IsEnabled = true,
            DiagnosticDataLevel = DeployOobeDiagnosticDataLevel.Off,
            LocationAccess = DeployOobeLocationAccessMode.ForceOff
        };
        DeployAppxRemovalSettings appxRemoval = new()
        {
            IsEnabled = true,
            PackageNames = ["Microsoft.BingNews", "Microsoft.BingWeather"]
        };
        DeployAiComponentRemovalSettings aiComponentRemoval = new()
        {
            IsEnabled = true,
            RemoveCopilot = true,
            DisableRecall = true
        };
        DeployWindowsOptionalFeatureSettings windowsOptionalFeatures = new()
        {
            IsEnabled = true,
            Actions = [new DeployWindowsOptionalFeatureAction { Id = "wf:netfx3", Enable = true }]
        };

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: targetDisk,
                targetComputerName: " LAB_01 ",
                driverPackSelectionKind: DriverPackSelectionKind.OemCatalog,
                selectedDriverPack: driverPack,
                isAutopilotEnabled: true,
                selectedAutopilotProfile: autopilotProfile,
                oobe: oobe,
                appxRemoval: appxRemoval,
                aiComponentRemoval: aiComponentRemoval,
                windowsOptionalFeatures: windowsOptionalFeatures));

        Assert.True(result.IsReadyToStart);
        Assert.Equal("LAB01", result.NormalizedComputerName);
        Assert.Equal(1, shell.ConfirmationCallCount);
        Assert.Equal(targetDisk.DiskNumber, result.Context?.TargetDiskNumber);
        Assert.Equal("LAB01", result.Context?.TargetComputerName);
        Assert.Same(driverPack, result.Context?.DriverPack);
        Assert.Same(autopilotProfile, result.Context?.SelectedAutopilotProfile);
        Assert.Same(oobe, result.Context?.Oobe);
        Assert.Same(appxRemoval, result.Context?.AppxRemoval);
        Assert.Same(aiComponentRemoval, result.Context?.AiComponentRemoval);
        Assert.Same(windowsOptionalFeatures, result.Context?.WindowsOptionalFeatures);
    }

    [Fact]
    public void Prepare_WhenCompletionRebootIsDisabled_RetainsConfiguredDelay()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);
        DeployCompletionSettings completion = new()
        {
            AutomaticRebootEnabled = false,
            AutomaticRebootDelaySeconds = 42
        };

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                completion: completion,
                isDryRun: true));

        Assert.True(result.IsReadyToStart);
        Assert.False(result.Context!.Completion.AutomaticRebootEnabled);
        Assert.Equal(42, result.Context.Completion.AutomaticRebootDelaySeconds);
    }

    [Fact]
    public void Prepare_WhenHardwareHashUploadModeHasNoJsonProfile_ReturnsDeploymentContext()
    {
        var shell = new FakeApplicationShellService { ConfirmationResult = true };
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                isAutopilotEnabled: true,
                autopilotProvisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
                selectedAutopilotProfile: null,
                isDryRun: true));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, result.Context?.AutopilotProvisioningMode);
        Assert.Null(result.Context?.SelectedAutopilotProfile);
    }

    [Fact]
    public void Prepare_WhenInteractiveHardwareHashUploadModeHasNoJsonProfile_ReturnsDeploymentContext()
    {
        var shell = new FakeApplicationShellService { ConfirmationResult = true };
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                isAutopilotEnabled: true,
                autopilotProvisioningMode: AutopilotProvisioningMode.InteractiveHardwareHashUpload,
                selectedAutopilotProfile: null,
                isDryRun: true));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, result.Context?.AutopilotProvisioningMode);
        Assert.Null(result.Context?.SelectedAutopilotProfile);
    }

    [Fact]
    public void Prepare_WhenLiveHardwareHashUploadModeIsSelected_DoesNotRequireJsonProfile()
    {
        var shell = new FakeApplicationShellService { ConfirmationResult = true };
        var service = new DeploymentLaunchPreparationService(shell);
        DeployAutopilotHardwareHashUploadSettings hardwareHashUpload = new()
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ActiveCertificateThumbprint = "ABCDEF123456",
            ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1),
            DefaultGroupTag = "Sales"
        };

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                isAutopilotEnabled: true,
                autopilotProvisioningMode: AutopilotProvisioningMode.HardwareHashUpload,
                selectedAutopilotProfile: null,
                autopilotHardwareHashUpload: hardwareHashUpload,
                isDryRun: false));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, result.Context?.AutopilotProvisioningMode);
        Assert.Null(result.Context?.SelectedAutopilotProfile);
        Assert.Same(hardwareHashUpload, result.Context?.AutopilotHardwareHashUpload);
        Assert.Equal(1, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenJsonProfileModeHasNoProfile_FailsValidation()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                isAutopilotEnabled: true,
                autopilotProvisioningMode: AutopilotProvisioningMode.JsonProfile,
                selectedAutopilotProfile: null));

        Assert.False(result.IsReadyToStart);
        Assert.Null(result.Context);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenConfirmationIsShown_UsesLocalizedWarningText()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        try
        {
            var shell = new FakeApplicationShellService { ConfirmationResult = true };
            var service = new DeploymentLaunchPreparationService(shell);

            TargetDiskInfo targetDisk = CreateDisk();

            service.Prepare(CreateRequest(selectedTargetDisk: targetDisk));

            Assert.Equal("Confirmer l’effacement du disque", shell.LastConfirmationTitle);
            Assert.Contains("Cela effacera toutes les données du disque sélectionné et installera le système d’exploitation sélectionné.", shell.LastConfirmationMessage);
            Assert.Contains("Disque : 3", shell.LastConfirmationMessage);
            Assert.Contains("Taille : 256,0 GiB", shell.LastConfirmationMessage);
            Assert.Contains("Continuer le déploiement ?", shell.LastConfirmationMessage);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static DeploymentLaunchRequest CreateRequest(
        TargetDiskInfo? selectedTargetDisk,
        string targetComputerName = "LAB-01",
        DriverPackSelectionKind driverPackSelectionKind = DriverPackSelectionKind.None,
        DriverPackCatalogItem? selectedDriverPack = null,
        bool isAutopilotEnabled = false,
        AutopilotProvisioningMode autopilotProvisioningMode = AutopilotProvisioningMode.JsonProfile,
        AutopilotProfileCatalogItem? selectedAutopilotProfile = null,
        DeployAutopilotHardwareHashUploadSettings? autopilotHardwareHashUpload = null,
        DeployOobeSettings? oobe = null,
        DeployAppxRemovalSettings? appxRemoval = null,
        DeployAiComponentRemovalSettings? aiComponentRemoval = null,
        DeployWindowsOptionalFeatureSettings? windowsOptionalFeatures = null,
        DeployCompletionSettings? completion = null,
        bool isDryRun = false)
    {
        return new DeploymentLaunchRequest
        {
            Mode = DeploymentMode.Usb,
            CacheRootPath = @"X:\Foundry\Runtime",
            TargetComputerName = targetComputerName,
            SelectedTargetDisk = selectedTargetDisk,
            SelectedOperatingSystem = new OperatingSystemCatalogItem
            {
                WindowsRelease = "11",
                ReleaseId = "24H2",
                Architecture = "x64",
                LanguageCode = "en-US",
                Language = "English",
                Edition = "Professional",
                LicenseChannel = "Retail",
                Build = "26100"
            },
            DriverPackSelectionKind = driverPackSelectionKind,
            SelectedDriverPack = selectedDriverPack,
            ApplyFirmwareUpdates = false,
            IsAutopilotEnabled = isAutopilotEnabled,
            AutopilotProvisioningMode = autopilotProvisioningMode,
            SelectedAutopilotProfile = selectedAutopilotProfile,
            AutopilotHardwareHashUpload = autopilotHardwareHashUpload ?? new DeployAutopilotHardwareHashUploadSettings(),
            Oobe = oobe ?? new DeployOobeSettings(),
            AppxRemoval = appxRemoval ?? new DeployAppxRemovalSettings(),
            AiComponentRemoval = aiComponentRemoval ?? new DeployAiComponentRemovalSettings(),
            WindowsOptionalFeatures = windowsOptionalFeatures ?? new DeployWindowsOptionalFeatureSettings(),
            Completion = completion ?? new DeployCompletionSettings(),
            IsDryRun = isDryRun
        };
    }

    private static TargetDiskInfo CreateDisk(bool isSelectable = true, string selectionWarning = "", ulong sizeBytes = 256UL * 1024UL * 1024UL * 1024UL)
    {
        return new TargetDiskInfo
        {
            Identity = new DiskIdentity(3, "", "SERIAL-3", "NVMe Disk", "NVMe", sizeBytes),
            DiskNumber = 3,
            FriendlyName = "NVMe Disk",
            BusType = "NVMe",
            SizeBytes = sizeBytes,
            IsSelectable = isSelectable,
            SelectionWarning = selectionWarning
        };
    }

    private sealed class FakeApplicationShellService : IApplicationShellService
    {
        public bool ConfirmationResult { get; init; } = true;

        public int ConfirmationCallCount { get; private set; }

        public string LastConfirmationTitle { get; private set; } = string.Empty;

        public string LastConfirmationMessage { get; private set; } = string.Empty;
        public Action? OnConfirm { get; init; }

        public void ShowAbout()
        {
        }

        public bool ConfirmWarning(string title, string message)
        {
            ConfirmationCallCount++;
            OnConfirm?.Invoke();
            LastConfirmationTitle = title;
            LastConfirmationMessage = message;
            return ConfirmationResult;
        }

        public void Shutdown()
        {
        }
    }
}

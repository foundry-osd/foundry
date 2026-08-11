// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Validation;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Performs destructive disk layout, offline Windows image servicing, boot configuration, and WinRE operations.
/// </summary>
public sealed class WindowsDeploymentService : IWindowsDeploymentService
{
    private const int EfiPartitionSizeMb = 260;
    private const int MsrPartitionSizeMb = 16;
    private const int RecoveryPartitionSizeMb = 5120;
    private const string RecoveryPartitionLabel = "Recovery";
    private const string RecoveryPartitionGuid = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string RecoveryPartitionAttributes = "0x8000000000000001";
    private const string WinReImageFileName = "winre.wim";
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsDeploymentService> _logger;
    private readonly UnattendDocumentService _unattendDocumentService;
    private readonly OobePolicyRegistryWriter _oobePolicyRegistryWriter;
    private readonly AiComponentRemovalRegistryWriter _aiComponentRemovalRegistryWriter;

    /// <summary>
    /// Initializes a Windows deployment service.
    /// </summary>
    /// <param name="processRunner">The process runner used for diskpart, DISM, bcdboot, and winrecfg.</param>
    /// <param name="logger">The logger used for deployment diagnostics.</param>
    public WindowsDeploymentService(IProcessRunner processRunner, ILogger<WindowsDeploymentService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
        _unattendDocumentService = new UnattendDocumentService();
        _oobePolicyRegistryWriter = new OobePolicyRegistryWriter(processRunner);
        _aiComponentRemovalRegistryWriter = new AiComponentRemovalRegistryWriter(processRunner);
    }

    /// <inheritdoc />
    public async Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        int diskNumber,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (diskNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diskNumber), "Target disk number must be 0 or greater.");
        }

        _logger.LogInformation(
            "Preparing target disk layout. DiskNumber={DiskNumber}, RecoveryPartitionSizeMb={RecoveryPartitionSizeMb}, WorkingDirectory={WorkingDirectory}",
            diskNumber,
            RecoveryPartitionSizeMb,
            workingDirectory);
        (char systemLetter, char windowsLetter, char recoveryLetter) = GetPartitionLetters();
        Directory.CreateDirectory(workingDirectory);

        string[] scriptLines =
        [
            // This is the destructive boundary of deployment: the selected disk is cleaned and repartitioned.
            $"select disk {diskNumber}",
            "online disk noerr",
            "attributes disk clear readonly noerr",
            "clean",
            "convert gpt",
            $"create partition efi size={EfiPartitionSizeMb}",
            "format quick fs=fat32 label=System",
            $"assign letter={systemLetter}",
            $"create partition msr size={MsrPartitionSizeMb}",
            $"create partition primary size={RecoveryPartitionSizeMb}",
            $"set id=\"{RecoveryPartitionGuid}\"",
            $"gpt attributes={RecoveryPartitionAttributes}",
            $"format quick fs=ntfs label={RecoveryPartitionLabel}",
            $"assign letter={recoveryLetter}",
            "create partition primary",
            "format quick fs=ntfs label=Windows",
            $"assign letter={windowsLetter}"
        ];

        string scriptPath = Path.Combine(workingDirectory, "diskpart-os-target.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);

        await RunRequiredProcessAsync(
            "diskpart.exe",
            $"/s \"{scriptPath}\"",
            workingDirectory,
            $"Disk partitioning failed for disk {diskNumber}",
            cancellationToken).ConfigureAwait(false);

        string systemPartitionRoot = $"{systemLetter}:\\";
        string windowsPartitionRoot = $"{windowsLetter}:\\";
        string recoveryPartitionRoot = $"{recoveryLetter}:\\";

        _logger.LogInformation(
            "Target disk layout prepared. DiskNumber={DiskNumber}, SystemPartition={SystemPartition}, WindowsPartition={WindowsPartition}, RecoveryPartition={RecoveryPartition}",
            diskNumber,
            systemPartitionRoot,
            windowsPartitionRoot,
            recoveryPartitionRoot);

        return new DeploymentTargetLayout
        {
            DiskNumber = diskNumber,
            SystemPartitionRoot = systemPartitionRoot,
            WindowsPartitionRoot = windowsPartitionRoot,
            RecoveryPartitionRoot = recoveryPartitionRoot,
            RecoveryPartitionLetter = recoveryLetter
        };
    }

    /// <inheritdoc />
    public async Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Operating system image was not found.", imagePath);
        }

        _logger.LogInformation("Resolving OS image index. ImagePath={ImagePath}, RequestedEdition={RequestedEdition}", imagePath, requestedEdition);
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "dism.exe",
                $"/English /Get-ImageInfo /ImageFile:\"{imagePath}\"",
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Failed to resolve OS image index for {ImagePath}. Diagnostic={Diagnostic}", imagePath, execution.ToDiagnosticText());
            throw new DeploymentProcessException(
                $"Unable to resolve image index for '{imagePath}'.{Environment.NewLine}{execution.ToDiagnosticText()}",
                execution.ExitCode);
        }

        IReadOnlyList<int> imageIndexes = ParseImageIndexes(execution.StandardOutput);
        if (imageIndexes.Count == 0)
        {
            throw new InvalidOperationException($"The operating system image does not expose any image indexes: '{imagePath}'.");
        }

        WindowsEditionDefinition? requestedDefinition = WindowsEditionCatalog.Find(requestedEdition);
        if (requestedDefinition is null)
        {
            throw new InvalidOperationException($"Windows edition '{requestedEdition}' is not supported.");
        }

        var imageMetadata = new List<ImageIndexMetadata>(imageIndexes.Count);
        foreach (int imageIndex in imageIndexes)
        {
            ProcessExecutionResult detailedExecution = await _processRunner
                .RunAsync(
                    "dism.exe",
                    $"/English /Get-ImageInfo /ImageFile:\"{imagePath}\" /Index:{imageIndex}",
                    workingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!detailedExecution.IsSuccess)
            {
                _logger.LogError(
                    "Failed to inspect OS image index {ImageIndex} for {ImagePath}. Diagnostic={Diagnostic}",
                    imageIndex,
                    imagePath,
                    detailedExecution.ToDiagnosticText());
                throw new DeploymentProcessException(
                    $"Unable to inspect image index {imageIndex} in '{imagePath}'.{Environment.NewLine}{detailedExecution.ToDiagnosticText()}",
                    detailedExecution.ExitCode);
            }

            imageMetadata.Add(new ImageIndexMetadata(imageIndex, ParseEditionId(detailedExecution.StandardOutput)));
        }

        ImageIndexMetadata[] matches = imageMetadata
            .Where(item => item.EditionId.Equals(requestedDefinition.EditionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length != 1)
        {
            string availableEditionIds = string.Join(
                ", ",
                imageMetadata.Select(item => $"{item.Index}: {item.EditionId}"));

            throw new InvalidOperationException(
                $"Expected exactly one '{requestedDefinition.EditionId}' image for Windows edition '{requestedDefinition.Name}' in '{imagePath}', " +
                $"but found {matches.Length}. Available edition IDs: {availableEditionIds}.");
        }

        int resolvedIndex = matches[0].Index;
        _logger.LogInformation("Resolved OS image index {ImageIndex} for ImagePath={ImagePath}", resolvedIndex, imagePath);
        return resolvedIndex;
    }

    /// <inheritdoc />
    public async Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        _logger.LogInformation("Applying OS image. ImagePath={ImagePath}, Index={ImageIndex}, WindowsPartitionRoot={WindowsPartitionRoot}",
            imagePath,
            imageIndex,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        string[] arguments =
        [
            "/Apply-Image",
            $"/ImageFile:{imagePath}",
            $"/Index:{imageIndex}",
            $"/ApplyDir:{windowsPartitionRoot}",
            "/CheckIntegrity",
            $"/ScratchDir:{scratchDirectory}"
        ];

        if (progress is null)
        {
            await RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DismProgressReporter progressReporter = new(progress);
            await RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken,
                progressReporter.HandleOutput,
                progressReporter.HandleOutput).ConfigureAwait(false);

            if (progressReporter.HasReportedProgress)
            {
                progress.Report(100d);
            }
        }

        _logger.LogInformation("OS image apply completed. ImagePath={ImagePath}, Index={ImageIndex}", imagePath, imageIndex);
    }

    /// <inheritdoc />
    public async Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        string[] arguments =
        [
            "/English",
            $"/Image:{windowsPartitionRoot}",
            "/Get-CurrentEdition"
        ];

        ProcessExecutionResult execution = await RunRequiredProcessAsync(
            "dism.exe",
            arguments,
            workingDirectory,
            "Failed to query the applied Windows edition",
            cancellationToken).ConfigureAwait(false);

        Match editionMatch = Regex.Match(
            execution.StandardOutput,
            @"Current\s+Edition\s*:\s*(.+)",
            RegexOptions.IgnoreCase);

        if (!editionMatch.Success)
        {
            _logger.LogWarning("Unable to parse the applied Windows edition from DISM output.");
            return null;
        }

        string edition = editionMatch.Groups[1].Value.Trim();
        if (edition.Length == 0)
        {
            return null;
        }

        _logger.LogInformation("Detected applied Windows edition. Edition={Edition}", edition);
        return edition;
    }

    /// <inheritdoc />
    public Task ConfigureOfflineComputerNameAsync(
        string windowsPartitionRoot,
        string computerName,
        string processorArchitecture,
        string? defaultTimeZoneId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        if (!ComputerNameRules.IsValid(computerName))
        {
            throw new ArgumentException(
                "Computer name must contain 1 to 15 valid characters (letters, numbers, or hyphen).",
                nameof(computerName));
        }

        if (string.IsNullOrWhiteSpace(processorArchitecture))
        {
            _logger.LogWarning("Processor architecture was not provided when configuring the offline computer name. Falling back to amd64.");
        }

        // The specialize pass is used so computer name and time zone are applied before OOBE starts.
        XNamespace unattendNamespace = UnattendDocumentService.Namespace;
        XDocument document = _unattendDocumentService.LoadOrCreate(windowsPartitionRoot);
        XElement component = _unattendDocumentService.EnsureShellSetupComponent(document, "specialize", processorArchitecture);

        XElement computerNameElement = component.Element(unattendNamespace + "ComputerName")
            ?? new XElement(unattendNamespace + "ComputerName");

        if (computerNameElement.Parent is null)
        {
            component.Add(computerNameElement);
        }

        computerNameElement.Value = computerName;

        XElement timeZoneElement = component.Element(unattendNamespace + "TimeZone")
            ?? new XElement(unattendNamespace + "TimeZone");

        string? unattendTimeZoneId = ResolveUnattendTimeZoneId(defaultTimeZoneId);
        if (string.IsNullOrWhiteSpace(unattendTimeZoneId))
        {
            if (timeZoneElement.Parent is not null)
            {
                timeZoneElement.Remove();
            }
        }
        else
        {
            if (timeZoneElement.Parent is null)
            {
                component.Add(timeZoneElement);
            }

            timeZoneElement.Value = unattendTimeZoneId;
        }

        _unattendDocumentService.Save(windowsPartitionRoot, document);

        _logger.LogInformation(
            "Offline computer name configured. ComputerName={ComputerName}, UnattendPath={UnattendPath}, ProcessorArchitecture={ProcessorArchitecture}, DefaultTimeZoneConfigured={DefaultTimeZoneConfigured}",
            computerName,
            Path.Combine(windowsPartitionRoot, "Windows", "Panther", "unattend.xml"),
            processorArchitecture,
            !string.IsNullOrWhiteSpace(unattendTimeZoneId));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ConfigureOfflineOobeAsync(
        string windowsPartitionRoot,
        DeployOobeSettings settings,
        string processorArchitecture,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        Directory.CreateDirectory(workingDirectory);

        if (!settings.IsEnabled)
        {
            _logger.LogInformation("OOBE customization is disabled.");
            return;
        }

        XNamespace unattendNamespace = UnattendDocumentService.Namespace;
        XDocument document = _unattendDocumentService.LoadOrCreate(windowsPartitionRoot);
        XElement component = _unattendDocumentService.EnsureShellSetupComponent(document, "oobeSystem", processorArchitecture);
        XElement oobeElement = component.Element(unattendNamespace + "OOBE") ?? new XElement(unattendNamespace + "OOBE");
        if (oobeElement.Parent is null)
        {
            component.Add(oobeElement);
        }

        SetElementValue(oobeElement, unattendNamespace, "HideEULAPage", settings.SkipLicenseTerms ? "true" : "false");
        if (settings.HidePrivacySetup)
        {
            SetElementValue(oobeElement, unattendNamespace, "ProtectYourPC", "3");
        }
        else
        {
            RemoveElement(oobeElement, unattendNamespace, "ProtectYourPC");
        }

        _unattendDocumentService.Save(windowsPartitionRoot, document);

        await _oobePolicyRegistryWriter
            .ApplyAsync(windowsPartitionRoot, settings, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Offline OOBE customization configured. WindowsPartitionRoot={WindowsPartitionRoot}, DiagnosticDataLevel={DiagnosticDataLevel}, LocationAccess={LocationAccess}",
            windowsPartitionRoot,
            settings.DiagnosticDataLevel,
            settings.LocationAccess);
    }

    /// <inheritdoc />
    public async Task ConfigureOfflineAiComponentRemovalAsync(
        string windowsPartitionRoot,
        DeployAiComponentRemovalSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        Directory.CreateDirectory(workingDirectory);

        if (!settings.IsEnabled || !HasAnyAiPolicyOptionEnabled(settings))
        {
            _logger.LogInformation("AI policy customization is disabled.");
            return;
        }

        await _aiComponentRemovalRegistryWriter
            .ApplyAsync(windowsPartitionRoot, settings, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Offline AI policy customization configured. WindowsPartitionRoot={WindowsPartitionRoot}, RemoveCopilot={RemoveCopilot}, DisableRecall={DisableRecall}, DisableClickToDo={DisableClickToDo}, DisableAiServiceAutoStart={DisableAiServiceAutoStart}, DisableEdgeAi={DisableEdgeAi}, DisablePaintAi={DisablePaintAi}, DisableNotepadAi={DisableNotepadAi}",
            windowsPartitionRoot,
            settings.RemoveCopilot,
            settings.DisableRecall,
            settings.DisableClickToDo,
            settings.DisableAiServiceAutoStart,
            settings.DisableEdgeAi,
            settings.DisablePaintAi,
            settings.DisableNotepadAi);
    }

    /// <inheritdoc />
    public async Task<WindowsOptionalFeatureServicingResult> ConfigureOfflineWindowsOptionalFeaturesAsync(
        string setupMediaImagePath,
        string windowsPartitionRoot,
        DeployWindowsOptionalFeatureSettings settings,
        string scratchDirectory,
        string sourceExtractionDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null,
        Action? onInspectionStarted = null,
        Action? onSourcePreparationStarted = null,
        Action? onServicingStarted = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DeployWindowsOptionalFeatureAction[] requestedActions = settings.Actions?.ToArray() ?? [];
        if (!settings.IsEnabled || requestedActions.Length == 0)
        {
            return new WindowsOptionalFeatureServicingResult();
        }

        WindowsOptionalFeatureWorkItem[] requestedItems = ResolveWindowsOptionalFeatureActions(requestedActions);
        string cleanupRoot = Path.GetFullPath(Path.Combine(workingDirectory, ".."));

        try
        {
            Directory.CreateDirectory(scratchDirectory);
            Directory.CreateDirectory(workingDirectory);

            onInspectionStarted?.Invoke();
            IReadOnlyDictionary<string, OfflineWindowsFeatureState> initialStates =
                await GetOfflineWindowsFeatureStatesAsync(windowsPartitionRoot, workingDirectory, cancellationToken)
                    .ConfigureAwait(false);

            List<WindowsOptionalFeatureWorkItem> pendingItems = [];
            List<string> unavailableEnableActionIds = [];
            int alreadySatisfiedCount = 0;
            foreach (WindowsOptionalFeatureWorkItem item in requestedItems)
            {
                if (!initialStates.TryGetValue(item.CatalogEntry.FeatureName, out OfflineWindowsFeatureState state))
                {
                    if (item.Action.Enable)
                    {
                        unavailableEnableActionIds.Add(item.Action.Id);
                        _logger.LogWarning(
                            "Requested Windows optional feature is not present in the applied image. FeatureId={FeatureId}",
                            item.Action.Id);
                    }
                    else
                    {
                        alreadySatisfiedCount++;
                    }

                    continue;
                }

                if (IsRequestedStateSatisfied(item.Action.Enable, state))
                {
                    alreadySatisfiedCount++;
                    continue;
                }

                WindowsOptionalFeatureCatalogEntry effectiveEntry =
                    WindowsOptionalFeatureCatalog.GetEffectiveEntry(item.CatalogEntry.Id) ?? item.CatalogEntry;
                if (item.Action.Enable &&
                    state == OfflineWindowsFeatureState.PayloadRemoved &&
                    !effectiveEntry.RequiresSetupMediaSxs)
                {
                    throw new InvalidOperationException(
                        $"Windows optional feature '{item.CatalogEntry.FeatureName}' has a removed payload and no supported local source mapping.");
                }

                pendingItems.Add(item with { CatalogEntry = effectiveEntry });
            }

            bool matchingSourceUsed = pendingItems.Any(item => item.Action.Enable && item.CatalogEntry.RequiresSetupMediaSxs);
            string? sourcePath = null;
            if (matchingSourceUsed)
            {
                if (!File.Exists(setupMediaImagePath))
                {
                    throw new FileNotFoundException(
                        "The setup-media image required for Windows optional feature servicing was not found.",
                        setupMediaImagePath);
                }

                onSourcePreparationStarted?.Invoke();
                SetupMediaImageMetadata metadata = await ResolveSetupMediaImageMetadataAsync(
                        setupMediaImagePath,
                        workingDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                await RunRequiredProcessAsync(
                    "dism.exe",
                    [
                        "/English",
                        "/Apply-Image",
                        $"/ImageFile:{setupMediaImagePath}",
                        $"/Index:{metadata.Index}",
                        $"/ApplyDir:{sourceExtractionDirectory}",
                        "/CheckIntegrity",
                        $"/ScratchDir:{scratchDirectory}"
                    ],
                    workingDirectory,
                    $"Failed to extract Windows Setup Media from '{setupMediaImagePath}'",
                    cancellationToken).ConfigureAwait(false);

                sourcePath = ValidateMatchingNetFx3Source(
                    setupMediaImagePath,
                    sourceExtractionDirectory,
                    metadata);
            }

            WindowsOptionalFeatureWorkItem[] orderedPendingItems =
            [
                .. pendingItems
                    .Where(item => item.Action.Enable)
                    .OrderBy(item => item.Depth)
                    .ThenBy(item => item.CatalogEntry.SortOrder),
                .. pendingItems
                    .Where(item => !item.Action.Enable)
                    .OrderByDescending(item => item.Depth)
                    .ThenBy(item => item.CatalogEntry.SortOrder)
            ];

            if (orderedPendingItems.Length > 0)
            {
                onServicingStarted?.Invoke();
            }

            for (int index = 0; index < orderedPendingItems.Length; index++)
            {
                WindowsOptionalFeatureWorkItem item = orderedPendingItems[index];
                List<string> arguments =
                [
                    "/English",
                    $"/Image:{windowsPartitionRoot}",
                    item.Action.Enable ? "/Enable-Feature" : "/Disable-Feature",
                    $"/FeatureName:{item.CatalogEntry.FeatureName}"
                ];
                if (item.Action.Enable)
                {
                    arguments.Add("/All");
                }

                arguments.Add("/NoRestart");
                if (item.Action.Enable)
                {
                    arguments.Add("/LimitAccess");
                    if (item.CatalogEntry.RequiresSetupMediaSxs)
                    {
                        arguments.Add($"/Source:{sourcePath}");
                    }
                }

                arguments.Add($"/ScratchDir:{scratchDirectory}");
                await RunRequiredProcessAsync(
                    "dism.exe",
                    arguments,
                    workingDirectory,
                    $"Failed to {(item.Action.Enable ? "enable" : "disable")} Windows optional feature '{item.CatalogEntry.FeatureName}'",
                    cancellationToken).ConfigureAwait(false);
                progress?.Report((index + 1d) / orderedPendingItems.Length * 100d);
            }

            if (orderedPendingItems.Length > 0)
            {
                IReadOnlyDictionary<string, OfflineWindowsFeatureState> finalStates =
                    await GetOfflineWindowsFeatureStatesAsync(windowsPartitionRoot, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);
                foreach (WindowsOptionalFeatureWorkItem item in orderedPendingItems)
                {
                    if (!finalStates.TryGetValue(item.CatalogEntry.FeatureName, out OfflineWindowsFeatureState finalState) ||
                        !IsRequestedStateSatisfied(item.Action.Enable, finalState))
                    {
                        throw new InvalidOperationException(
                            $"Windows optional feature verification failed for '{item.CatalogEntry.FeatureName}'.");
                    }
                }
            }

            return new WindowsOptionalFeatureServicingResult
            {
                RequestedActionCount = requestedItems.Length,
                ChangedActionCount = orderedPendingItems.Length,
                AlreadySatisfiedActionCount = alreadySatisfiedCount,
                UnavailableEnableActionIds = unavailableEnableActionIds,
                MatchingSourceUsed = matchingSourceUsed
            };
        }
        finally
        {
            TryCleanupOptionalFeatureDirectory(scratchDirectory, cleanupRoot);
            TryCleanupOptionalFeatureDirectory(sourceExtractionDirectory, cleanupRoot);
        }
    }

    private static bool HasAnyAiPolicyOptionEnabled(DeployAiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableAiServiceAutoStart ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }

    private static string? ResolveUnattendTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        string normalizedTimeZoneId = timeZoneId.Trim();
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(normalizedTimeZoneId, out string? windowsTimeZoneId) &&
            !string.IsNullOrWhiteSpace(windowsTimeZoneId))
        {
            return windowsTimeZoneId;
        }

        return normalizedTimeZoneId.Contains('/', StringComparison.Ordinal)
            ? null
            : normalizedTimeZoneId;
    }

    /// <inheritdoc />
    public async Task ConfigureRecoveryEnvironmentAsync(
        string windowsPartitionRoot,
        string recoveryPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        Directory.CreateDirectory(workingDirectory);

        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        string sourceWinRePath = Path.Combine(windowsPath, "System32", "Recovery", WinReImageFileName);
        if (!File.Exists(sourceWinRePath))
        {
            throw new FileNotFoundException("The offline Windows image does not contain winre.wim.", sourceWinRePath);
        }

        string recoveryDirectory = GetRecoveryDirectoryPath(recoveryPartitionRoot);
        Directory.CreateDirectory(recoveryDirectory);

        string targetWinRePath = GetRecoveryImagePath(recoveryPartitionRoot);
        File.Copy(sourceWinRePath, targetWinRePath, overwrite: true);

        _logger.LogInformation(
            "Configuring recovery environment. WindowsPath={WindowsPath}, RecoveryDirectory={RecoveryDirectory}",
            windowsPath,
            recoveryDirectory);

        string winReConfigToolPath = ResolveRequiredWinReConfigToolPath();

        await RunRequiredProcessAsync(
            winReConfigToolPath,
            ["/setreimage", "/path", recoveryDirectory, "/target", windowsPath],
            workingDirectory,
            "Failed to set the Windows RE image location",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Recovery environment configured successfully.");
    }

    /// <inheritdoc />
    public async Task SealRecoveryPartitionAsync(
        string recoveryPartitionRoot,
        char recoveryPartitionLetter,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        char normalizedLetter = char.ToUpperInvariant(recoveryPartitionLetter);
        Directory.CreateDirectory(workingDirectory);

        string[] scriptLines =
        [
            $"select volume {normalizedLetter}",
            $"remove letter={normalizedLetter} noerr"
        ];

        string scriptPath = Path.Combine(workingDirectory, "diskpart-hide-recovery.txt");
        await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);

        await RunRequiredProcessAsync(
            "diskpart.exe",
            $"/s \"{scriptPath}\"",
            workingDirectory,
            "Failed to hide the recovery partition",
            cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(recoveryPartitionRoot))
        {
            throw new InvalidOperationException($"Recovery partition letter '{normalizedLetter}' is still accessible after sealing.");
        }

        _logger.LogInformation("Recovery partition sealed successfully. RecoveryPartitionLetter={RecoveryPartitionLetter}", normalizedLetter);
    }

    /// <inheritdoc />
    public async Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        _logger.LogInformation("Applying offline drivers. DriverRoot={DriverRoot}, WindowsPartitionRoot={WindowsPartitionRoot}",
            driverRoot,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        if (progress is null)
        {
            await RunRequiredProcessAsync(
                "dism.exe",
                [
                    $"/Image:{windowsPartitionRoot}",
                    "/Add-Driver",
                    $"/Driver:{driverRoot}",
                    "/Recurse",
                    $"/ScratchDir:{scratchDirectory}"
                ],
                workingDirectory,
                $"Offline driver injection failed for '{driverRoot}'",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DismProgressReporter progressReporter = new(progress);
            await RunRequiredProcessAsync(
                "dism.exe",
                [
                    $"/Image:{windowsPartitionRoot}",
                    "/Add-Driver",
                    $"/Driver:{driverRoot}",
                    "/Recurse",
                    $"/ScratchDir:{scratchDirectory}"
                ],
                workingDirectory,
                $"Offline driver injection failed for '{driverRoot}'",
                cancellationToken,
                progressReporter.HandleOutput,
                progressReporter.HandleOutput).ConfigureAwait(false);

            if (progressReporter.HasReportedProgress)
            {
                progress.Report(100d);
            }
        }

        _logger.LogInformation("Offline driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    /// <inheritdoc />
    public async Task ApplyRecoveryDriversAsync(
        string recoveryPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? mountProgress = null,
        IProgress<double>? applyProgress = null,
        IProgress<double>? unmountProgress = null,
        Action? onMountStarted = null,
        Action? onApplyStarted = null,
        Action? onUnmountStarted = null)
    {
        if (string.IsNullOrWhiteSpace(recoveryPartitionRoot))
        {
            throw new ArgumentException("Recovery partition root is required.", nameof(recoveryPartitionRoot));
        }

        if (string.IsNullOrWhiteSpace(driverRoot))
        {
            throw new ArgumentException("Driver root is required.", nameof(driverRoot));
        }

        string winReImagePath = GetRecoveryImagePath(recoveryPartitionRoot);
        if (!File.Exists(winReImagePath))
        {
            throw new FileNotFoundException("The recovery partition does not contain winre.wim.", winReImagePath);
        }

        Directory.CreateDirectory(scratchDirectory);
        Directory.CreateDirectory(workingDirectory);

        string mountPath = Path.Combine(workingDirectory, "Mount-WindowsRE");
        ResetWorkingDirectory(mountPath);

        _logger.LogInformation(
            "Applying recovery drivers. DriverRoot={DriverRoot}, WinReImagePath={WinReImagePath}, MountPath={MountPath}",
            driverRoot,
            winReImagePath,
            mountPath);

        Exception? pendingException = null;
        bool mounted = false;
        bool shouldCommit = false;

        try
        {
            string[] mountArguments =
            [
                "/Mount-Image",
                $"/ImageFile:{winReImagePath}",
                "/Index:1",
                $"/MountDir:{mountPath}",
                $"/ScratchDir:{scratchDirectory}"
            ];

            onMountStarted?.Invoke();
            DismProgressReporter? mountProgressReporter = null;
            if (mountProgress is null)
            {
                await RunRequiredProcessAsync(
                    "dism.exe",
                    mountArguments,
                    workingDirectory,
                    "Failed to mount the Windows RE image",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                mountProgressReporter = new(mountProgress);
                await RunRequiredProcessAsync(
                    "dism.exe",
                    mountArguments,
                    workingDirectory,
                    "Failed to mount the Windows RE image",
                    cancellationToken,
                    mountProgressReporter.HandleOutput,
                    mountProgressReporter.HandleOutput).ConfigureAwait(false);
            }

            mounted = true;
            if (mountProgressReporter is not null && mountProgressReporter.HasReportedProgress)
            {
                mountProgress!.Report(100d);
            }

            onApplyStarted?.Invoke();
            DismProgressReporter? progressReporter = null;
            if (applyProgress is null)
            {
                await RunRequiredProcessAsync(
                    "dism.exe",
                    [
                        $"/Image:{mountPath}",
                        "/Add-Driver",
                        $"/Driver:{driverRoot}",
                        "/Recurse",
                        $"/ScratchDir:{scratchDirectory}"
                    ],
                    workingDirectory,
                    $"Recovery driver injection failed for '{driverRoot}'",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                progressReporter = new(applyProgress);
                await RunRequiredProcessAsync(
                    "dism.exe",
                    [
                        $"/Image:{mountPath}",
                        "/Add-Driver",
                        $"/Driver:{driverRoot}",
                        "/Recurse",
                        $"/ScratchDir:{scratchDirectory}"
                    ],
                    workingDirectory,
                    $"Recovery driver injection failed for '{driverRoot}'",
                    cancellationToken,
                    progressReporter.HandleOutput,
                    progressReporter.HandleOutput).ConfigureAwait(false);
            }

            shouldCommit = true;
            if (progressReporter is not null && progressReporter.HasReportedProgress)
            {
                applyProgress!.Report(100d);
            }
        }
        catch (Exception ex)
        {
            pendingException = ex;
        }
        finally
        {
            if (mounted)
            {
                // Always unmount WinRE even after driver injection failure so the image is not left mounted.
                string[] unmountArguments = shouldCommit
                    ? ["/Unmount-Image", $"/MountDir:{mountPath}", "/Commit"]
                    : ["/Unmount-Image", $"/MountDir:{mountPath}", "/Discard"];

                onUnmountStarted?.Invoke();
                ProcessExecutionResult unmountExecution;
                DismProgressReporter? unmountProgressReporter = null;
                if (unmountProgress is null)
                {
                    unmountExecution = await _processRunner
                        .RunAsync("dism.exe", unmountArguments, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    unmountProgressReporter = new(unmountProgress);
                    unmountExecution = await _processRunner
                        .RunAsync(
                            "dism.exe",
                            unmountArguments,
                            workingDirectory,
                            unmountProgressReporter.HandleOutput,
                            unmountProgressReporter.HandleOutput,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!unmountExecution.IsSuccess)
                {
                    string diagnostic = unmountExecution.ToDiagnosticText();
                    _logger.LogError("Failed to unmount the Windows RE image. Diagnostic={Diagnostic}", diagnostic);

                    pendingException = pendingException is null
                        ? new DeploymentProcessException(
                            $"Failed to unmount the Windows RE image.{Environment.NewLine}{diagnostic}",
                            unmountExecution.ExitCode)
                        : new DeploymentProcessException(
                            $"Windows RE servicing failed and the image could not be unmounted cleanly.{Environment.NewLine}{diagnostic}",
                            unmountExecution.ExitCode,
                            pendingException);
                }
                else
                {
                    if (unmountProgressReporter is not null && unmountProgressReporter.HasReportedProgress)
                    {
                        unmountProgress!.Report(100d);
                    }
                }
            }

            TryDeleteDirectory(mountPath);
        }

        if (pendingException is not null)
        {
            throw pendingException;
        }

        _logger.LogInformation("Recovery driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    /// <inheritdoc />
    public async Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        int operatingSystemBuildMajor,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        string bcdBootPath = Path.Combine(windowsPath, "System32", "bcdboot.exe");
        if (!File.Exists(bcdBootPath))
        {
            throw new FileNotFoundException(
                "The applied Windows image does not contain bcdboot.exe.",
                bcdBootPath);
        }

        _logger.LogInformation("Configuring boot files. WindowsPath={WindowsPath}, SystemPartitionRoot={SystemPartitionRoot}", windowsPath, systemPartitionRoot);

        string arguments = operatingSystemBuildMajor >= 26200
            ? $"\"{windowsPath}\" /s \"{systemPartitionRoot}\" /f UEFI /c /bootex /v"
            : $"\"{windowsPath}\" /s \"{systemPartitionRoot}\" /f UEFI /c /v";

        await RunRequiredProcessAsync(
            bcdBootPath,
            arguments,
            workingDirectory,
            "BCDBoot configuration failed",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("BCDBoot configuration completed successfully.");
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("{FailureSummary}. Diagnostic={Diagnostic}", failureSummary, execution.ToDiagnosticText());
            throw new DeploymentProcessException(
                $"{failureSummary}.{Environment.NewLine}{execution.ToDiagnosticText()}",
                execution.ExitCode);
        }

        return execution;
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        return await RunRequiredProcessAsync(
            fileName,
            arguments,
            workingDirectory,
            failureSummary,
            cancellationToken,
            onOutputData: null,
            onErrorData: null).ConfigureAwait(false);
    }

    private async Task<ProcessExecutionResult> RunRequiredProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureSummary,
        CancellationToken cancellationToken,
        Action<string>? onOutputData,
        Action<string>? onErrorData)
    {
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, onOutputData, onErrorData, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("{FailureSummary}. Diagnostic={Diagnostic}", failureSummary, execution.ToDiagnosticText());
            throw new DeploymentProcessException(
                $"{failureSummary}.{Environment.NewLine}{execution.ToDiagnosticText()}",
                execution.ExitCode);
        }

        return execution;
    }

    private static WindowsOptionalFeatureWorkItem[] ResolveWindowsOptionalFeatureActions(
        IReadOnlyList<DeployWindowsOptionalFeatureAction> actions)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<WindowsOptionalFeatureWorkItem>(actions.Count);
        foreach (DeployWindowsOptionalFeatureAction? action in actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Id))
            {
                throw new InvalidOperationException("Windows optional feature actions must contain a non-empty ID.");
            }

            WindowsOptionalFeatureCatalogEntry? entry = WindowsOptionalFeatureCatalog.Find(action.Id);
            if (entry is null)
            {
                throw new InvalidOperationException($"Unknown Windows optional feature ID '{action.Id}'.");
            }

            if (!seenIds.Add(entry.Id))
            {
                throw new InvalidOperationException($"Duplicate or conflicting Windows optional feature action '{entry.Id}'.");
            }

            items.Add(new WindowsOptionalFeatureWorkItem(
                new DeployWindowsOptionalFeatureAction { Id = entry.Id, Enable = action.Enable },
                entry,
                WindowsOptionalFeatureCatalog.GetDepth(entry.Id)));
        }

        HashSet<string> disabledIds = items
            .Where(item => !item.Action.Enable)
            .Select(item => item.CatalogEntry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (WindowsOptionalFeatureWorkItem enabledItem in items.Where(item => item.Action.Enable))
        {
            if (WindowsOptionalFeatureCatalog.GetAncestors(enabledItem.CatalogEntry.Id)
                .Any(ancestor => disabledIds.Contains(ancestor.Id)))
            {
                throw new InvalidOperationException(
                    $"Windows optional feature '{enabledItem.CatalogEntry.Id}' cannot be enabled beneath a disabled ancestor.");
            }
        }

        return items.ToArray();
    }

    private async Task<IReadOnlyDictionary<string, OfflineWindowsFeatureState>> GetOfflineWindowsFeatureStatesAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await RunRequiredProcessAsync(
            "dism.exe",
            [
                "/English",
                $"/Image:{windowsPartitionRoot}",
                "/Get-Features",
                "/Format:Table"
            ],
            workingDirectory,
            $"Failed to inspect Windows optional features in '{windowsPartitionRoot}'",
            cancellationToken).ConfigureAwait(false);
        return ParseOfflineWindowsFeatureStates(result.StandardOutput);
    }

    private static IReadOnlyDictionary<string, OfflineWindowsFeatureState> ParseOfflineWindowsFeatureStates(string output)
    {
        var states = new Dictionary<string, OfflineWindowsFeatureState>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     output ?? string.Empty,
                     @"^\s*(?<name>[^|\r\n]+?)\s*\|\s*(?<state>Enabled|Disabled|Enable Pending|Disable Pending|Disabled with Payload Removed)\s*$",
                     RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            string name = match.Groups["name"].Value.Trim();
            string stateText = match.Groups["state"].Value.Trim();
            OfflineWindowsFeatureState state = stateText.ToUpperInvariant() switch
            {
                "ENABLED" => OfflineWindowsFeatureState.Enabled,
                "DISABLED" => OfflineWindowsFeatureState.Disabled,
                "ENABLE PENDING" => OfflineWindowsFeatureState.EnablePending,
                "DISABLE PENDING" => OfflineWindowsFeatureState.DisablePending,
                "DISABLED WITH PAYLOAD REMOVED" => OfflineWindowsFeatureState.PayloadRemoved,
                _ => throw new InvalidOperationException($"Unsupported Windows optional feature state '{stateText}'.")
            };
            states[name] = state;
        }

        return states;
    }

    private static bool IsRequestedStateSatisfied(bool enable, OfflineWindowsFeatureState state)
    {
        return enable
            ? state is OfflineWindowsFeatureState.Enabled or OfflineWindowsFeatureState.EnablePending
            : state is OfflineWindowsFeatureState.Disabled or OfflineWindowsFeatureState.DisablePending or OfflineWindowsFeatureState.PayloadRemoved;
    }

    private async Task<SetupMediaImageMetadata> ResolveSetupMediaImageMetadataAsync(
        string imagePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult summary = await RunRequiredProcessAsync(
            "dism.exe",
            ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}"],
            workingDirectory,
            $"Failed to inspect setup-media image '{imagePath}'",
            cancellationToken).ConfigureAwait(false);
        (int Index, string Name)[] matches = Regex.Matches(
                summary.StandardOutput ?? string.Empty,
                @"^\s*Index\s*:\s*(?<index>\d+)\s*$\s*^\s*Name\s*:\s*(?<name>.+?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Select(match => (
                int.Parse(match.Groups["index"].Value),
                match.Groups["name"].Value.Trim()))
            .Where(item => string.Equals(item.Item2, "Windows Setup Media", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Setup-media image '{imagePath}' must contain exactly one image named 'Windows Setup Media'.");
        }

        ProcessExecutionResult detail = await RunRequiredProcessAsync(
            "dism.exe",
            ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}", $"/Index:{matches[0].Index}"],
            workingDirectory,
            $"Failed to inspect Windows Setup Media image index {matches[0].Index}",
            cancellationToken).ConfigureAwait(false);
        return new SetupMediaImageMetadata(
            matches[0].Index,
            ParseImageProperty(detail.StandardOutput, "Architecture"),
            ParseImageProperty(detail.StandardOutput, "Version"));
    }

    private static string ValidateMatchingNetFx3Source(
        string imagePath,
        string sourceExtractionDirectory,
        SetupMediaImageMetadata metadata)
    {
        string sourcePath = Path.Combine(sourceExtractionDirectory, "sources", "sxs");
        string architectureToken = metadata.Architecture.ToUpperInvariant() switch
        {
            "X64" or "AMD64" => "amd64",
            "ARM64" => "arm64",
            _ => throw new InvalidOperationException(
                $"Windows Setup Media in '{imagePath}' reports unsupported architecture '{metadata.Architecture}'.")
        };
        bool hasMatchingCab = Directory.Exists(sourcePath) && Directory
            .EnumerateFiles(sourcePath, "*.cab", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Any(fileName =>
                fileName is not null &&
                fileName.Contains("netfx3-ondemand-package", StringComparison.OrdinalIgnoreCase) &&
                fileName.Contains($"~{architectureToken}~", StringComparison.OrdinalIgnoreCase));
        if (!hasMatchingCab)
        {
            throw new InvalidOperationException(
                $"Matching NetFx3 source is unavailable. Media='{imagePath}', Version='{metadata.Version}', Architecture='{metadata.Architecture}', Expected='{architectureToken} NetFx3 OnDemand CAB'.");
        }

        return sourcePath;
    }

    private void TryCleanupOptionalFeatureDirectory(string path, string cleanupRoot)
    {
        try
        {
            string fullRoot = Path.GetFullPath(cleanupRoot);
            string fullPath = Path.GetFullPath(path);
            string relativePath = Path.GetRelativePath(fullRoot, fullPath);
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath == "." ||
                Path.IsPathRooted(relativePath) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                string.Equals(relativePath, "..", StringComparison.Ordinal))
            {
                _logger.LogWarning("Skipped optional-feature cleanup outside the deployment temp root. Path={Path}", fullPath);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean optional-feature temporary directory. Path={Path}", path);
        }
    }

    private static (char systemLetter, char windowsLetter, char recoveryLetter) GetPartitionLetters()
    {
        HashSet<char> usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        char systemLetter = GetAvailableLetter(usedLetters, ['S', 'T', 'U', 'V', 'W']);
        usedLetters.Add(systemLetter);

        char windowsLetter = GetAvailableLetter(usedLetters, ['W', 'V', 'U', 'T', 'Q', 'P']);
        usedLetters.Add(windowsLetter);

        char recoveryLetter = GetAvailableLetter(usedLetters, ['R', 'X', 'Y', 'Z']);
        return (systemLetter, windowsLetter, recoveryLetter);
    }

    private static char GetAvailableLetter(HashSet<char> usedLetters, IReadOnlyList<char> preferred)
    {
        foreach (char preferredLetter in preferred)
        {
            char letter = char.ToUpperInvariant(preferredLetter);
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        for (char letter = 'D'; letter <= 'Z'; letter++)
        {
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        throw new InvalidOperationException("No drive letter is available for deployment partitions.");
    }

    private static void ResetWorkingDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup; a later DISM failure will surface if the mount path is unusable.
        }
    }

    private static string GetRecoveryDirectoryPath(string recoveryPartitionRoot)
    {
        return Path.Combine(recoveryPartitionRoot, "Recovery", "WindowsRE");
    }

    private static string GetRecoveryImagePath(string recoveryPartitionRoot)
    {
        return Path.Combine(GetRecoveryDirectoryPath(recoveryPartitionRoot), WinReImageFileName);
    }

    private static string ResolveRequiredWinReConfigToolPath()
    {
        string path = Path.Combine(Environment.SystemDirectory, "winrecfg.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Required WinPE executable 'winrecfg.exe' was not found. Add the WinPE-WinReCfg optional component to the WinPE image.",
                path);
        }

        return path;
    }

    private static void SetElementValue(XElement parent, XNamespace elementNamespace, string elementName, string value)
    {
        XElement element = parent.Element(elementNamespace + elementName) ?? new XElement(elementNamespace + elementName);
        if (element.Parent is null)
        {
            parent.Add(element);
        }

        element.Value = value;
    }

    private static void RemoveElement(XElement parent, XNamespace elementNamespace, string elementName)
    {
        parent.Element(elementNamespace + elementName)?.Remove();
    }

    private static IReadOnlyList<int> ParseImageIndexes(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return Regex.Matches(output, @"^\s*Index\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Distinct()
            .ToArray();
    }

    private static string ParseEditionId(string output)
    {
        string editionId = ParseImageProperty(output, "Edition ID");
        return !string.IsNullOrWhiteSpace(editionId)
            ? editionId
            : ParseImageProperty(output, "Edition");
    }

    private static string ParseImageProperty(string output, string propertyName)
    {
        Match match = Regex.Match(
            output,
            $@"^\s*{Regex.Escape(propertyName)}\s*:\s*(.+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private sealed record ImageIndexMetadata(int Index, string EditionId);

    private sealed record WindowsOptionalFeatureWorkItem(
        DeployWindowsOptionalFeatureAction Action,
        WindowsOptionalFeatureCatalogEntry CatalogEntry,
        int Depth);

    private sealed record SetupMediaImageMetadata(int Index, string Architecture, string Version);

    private enum OfflineWindowsFeatureState
    {
        Enabled,
        Disabled,
        EnablePending,
        DisablePending,
        PayloadRemoved
    }
}

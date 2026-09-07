// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using Foundry.Deploy.Services.Autopilot;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Deployment.Unattend;
using ComputerNameRules = Foundry.Core.Services.Configuration.ComputerNameRules;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Owns offline Windows unattend, account, policy and optional feature settings.</summary>
public sealed class OfflineWindowsSettingsService : IOfflineWindowsSettingsService
{
    private static readonly TimeSpan MetadataExecutionTimeout = TimeSpan.FromMinutes(2);
    private readonly ILogger<OfflineWindowsSettingsService> _logger;
    private readonly WindowsDeploymentCommandRunner _commands;
    private readonly UnattendDocumentService _unattendDocumentService = new();
    private readonly OobePolicyRegistryWriter _oobePolicyRegistryWriter;
    private readonly AiComponentRemovalRegistryWriter _aiComponentRemovalRegistryWriter;
    private readonly IDeploymentSecretKeyProvider? _deploymentSecretKeyProvider;
    /// <summary>Creates the offline settings owner with bounded native commands and protected secret access.</summary>
    public OfflineWindowsSettingsService(IProcessRunner processRunner, ILogger<OfflineWindowsSettingsService> logger,
        IDeploymentSecretKeyProvider? deploymentSecretKeyProvider = null)
    {
        _logger = logger;
        _commands = new(processRunner, logger);
        _oobePolicyRegistryWriter = new(processRunner);
        _aiComponentRemovalRegistryWriter = new(processRunner);
        _deploymentSecretKeyProvider = deploymentSecretKeyProvider;
    }
    private const string AdministratorActivationDescription = "Enable built-in Administrator account";
    private const string AdministratorActivationCommand =
        "powershell.exe -NoProfile -NonInteractive -Command \"Get-LocalUser|Where-Object SID -like '*-500'|Enable-LocalUser -ErrorAction Stop\"";
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
        string workspaceRootPath,
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

        SetElementValue(
            oobeElement,
            unattendNamespace,
            "HideOnlineAccountScreens",
            ShouldHideOnlineAccountScreens(settings) ? "true" : "false");

        await ApplyOobeAccountsAsync(
            document,
            component,
            settings,
            processorArchitecture,
            workspaceRootPath,
            cancellationToken).ConfigureAwait(false);

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

    private async Task ApplyOobeAccountsAsync(
        XDocument document,
        XElement oobeSystemComponent,
        DeployOobeSettings settings,
        string processorArchitecture,
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        byte[]? deploymentKey = null;
        try
        {
            if (RequiresDeploymentKey(settings))
            {
                if (_deploymentSecretKeyProvider is null)
                {
                    throw new InvalidOperationException("A deployment secret key provider is required for OOBE account passwords.");
                }

                if (string.IsNullOrWhiteSpace(workspaceRootPath))
                {
                    throw new ArgumentException("Deployment workspace root is required for encrypted OOBE account passwords.", nameof(workspaceRootPath));
                }

                deploymentKey = await _deploymentSecretKeyProvider.ReadAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false);
            }

            WriteUserAccounts(oobeSystemComponent, settings, deploymentKey);
            if (settings.EnableAdministratorAccount)
            {
                WriteAdministratorActivation(document, processorArchitecture);
            }
            else
            {
                RemoveAdministratorActivation(document);
            }
        }
        finally
        {
            if (deploymentKey is not null)
            {
                CryptographicOperations.ZeroMemory(deploymentKey);
            }
        }
    }

    private static bool RequiresDeploymentKey(DeployOobeSettings settings) =>
        settings.AdministratorPasswordSecret is not null ||
        settings.AdditionalAccounts.Any(account => account.PasswordSecret is not null);

    private static bool ShouldHideOnlineAccountScreens(DeployOobeSettings settings) =>
        settings.AdditionalAccounts.Count > 0;

    private static void WriteUserAccounts(
        XElement component,
        DeployOobeSettings settings,
        byte[]? deploymentKey)
    {
        XNamespace ns = UnattendDocumentService.Namespace;
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        XElement userAccounts = component.Element(ns + "UserAccounts") ?? new XElement(ns + "UserAccounts");

        if (settings.EnableAdministratorAccount)
        {
            char[] password = DecryptPassword(settings.AdministratorPasswordIsBlank, settings.AdministratorPasswordSecret, deploymentKey);
            try
            {
                userAccounts.Element(ns + "AdministratorPassword")?.Remove();
                userAccounts.Add(CreatePasswordElement(ns, "AdministratorPassword", password, "AdministratorPassword"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            }
        }
        if (settings.AdditionalAccounts.Count > 0)
        {
            XElement localAccounts = userAccounts.Element(ns + "LocalAccounts") ?? new XElement(ns + "LocalAccounts");
            foreach (DeployOobeAdditionalAccountSettings account in settings.AdditionalAccounts)
            {
                char[] password = DecryptPassword(account.PasswordIsBlank, account.PasswordSecret, deploymentKey);
                try
                {
                    localAccounts.Elements(ns + "LocalAccount")
                        .Where(element => string.Equals(
                            element.Element(ns + "Name")?.Value,
                            account.UserName,
                            StringComparison.OrdinalIgnoreCase))
                        .Remove();
                    localAccounts.Add(
                        new XElement(ns + "LocalAccount",
                            new XAttribute(wcm + "action", "add"),
                            CreatePasswordElement(ns, "Password", password, "Password"),
                            new XElement(ns + "Description", account.UserName),
                            new XElement(ns + "DisplayName", account.UserName),
                            new XElement(ns + "Group", account.Type == OobeAccountType.Administrator ? "Administrators" : "Users"),
                            new XElement(ns + "Name", account.UserName)));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
                }
            }

            if (localAccounts.Parent is null)
            {
                userAccounts.Add(localAccounts);
            }
        }
        if (userAccounts.Parent is null && userAccounts.Elements().Any())
        {
            component.Add(userAccounts);
        }
    }

    private static char[] DecryptPassword(
        bool isBlank,
        Foundry.Deploy.Models.Configuration.SecretEnvelope? secret,
        byte[]? deploymentKey)
    {
        if (isBlank)
        {
            return [];
        }

        if (secret is null || deploymentKey is null)
        {
            throw new InvalidOperationException("An encrypted OOBE account password is missing.");
        }

        return DeployMediaSecretEnvelopeProtector.DecryptDeployChars(secret, deploymentKey);
    }

    private static XElement CreatePasswordElement(
        XNamespace ns,
        string elementName,
        ReadOnlySpan<char> password,
        string hiddenValueSuffix)
    {
        if (password.IsEmpty)
        {
            return new XElement(ns + elementName,
                new XElement(ns + "Value", string.Empty),
                new XElement(ns + "PlainText", "true"));
        }

        return new XElement(ns + elementName,
            new XElement(ns + "Value", EncodeHiddenUnattendPassword(password, hiddenValueSuffix)),
            new XElement(ns + "PlainText", "false"));
    }

    private static string EncodeHiddenUnattendPassword(ReadOnlySpan<char> password, string suffix)
    {
        char[] value = new char[password.Length + suffix.Length];
        byte[]? bytes = null;
        try
        {
            password.CopyTo(value);
            suffix.AsSpan().CopyTo(value.AsSpan(password.Length));
            bytes = Encoding.Unicode.GetBytes(value);
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private void WriteAdministratorActivation(XDocument document, string processorArchitecture)
    {
        XNamespace ns = UnattendDocumentService.Namespace;
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        XElement component = _unattendDocumentService.EnsureDeploymentComponent(document, "specialize", processorArchitecture);
        XElement runSynchronous = component.Element(ns + "RunSynchronous") ?? new XElement(ns + "RunSynchronous");
        runSynchronous.Elements(ns + "RunSynchronousCommand")
            .Where(IsAdministratorActivationCommand)
            .Remove();
        int order = runSynchronous.Elements(ns + "RunSynchronousCommand")
            .Select(element => int.TryParse(element.Element(ns + "Order")?.Value, out int value) ? value : 0)
            .DefaultIfEmpty()
            .Max() + 1;
        runSynchronous.Add(
            new XElement(ns + "RunSynchronousCommand",
                new XAttribute(wcm + "action", "add"),
                new XElement(ns + "Description", AdministratorActivationDescription),
                new XElement(ns + "Order", order),
                new XElement(ns + "Path", AdministratorActivationCommand)));
        if (runSynchronous.Parent is null)
        {
            component.Add(runSynchronous);
        }
    }

    private static void RemoveAdministratorActivation(XDocument document)
    {
        XNamespace ns = UnattendDocumentService.Namespace;
        XElement? component = FindDeploymentComponent(document, "specialize");
        if (component is null)
        {
            return;
        }

        XElement? runSynchronous = component.Element(ns + "RunSynchronous");
        runSynchronous?.Elements(ns + "RunSynchronousCommand")
            .Where(IsAdministratorActivationCommand)
            .Remove();
        if (runSynchronous is not null && !runSynchronous.Elements().Any())
        {
            runSynchronous.Remove();
        }
    }

    private static bool IsAdministratorActivationCommand(XElement element) =>
        string.Equals(
            element.Element(UnattendDocumentService.Namespace + "Description")?.Value,
            AdministratorActivationDescription,
            StringComparison.Ordinal);

    private static XElement? FindDeploymentComponent(XDocument document, string passName)
    {
        XNamespace ns = UnattendDocumentService.Namespace;
        return document.Root?
            .Elements(ns + "settings")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("pass")?.Value,
                passName,
                StringComparison.OrdinalIgnoreCase))?
            .Elements(ns + "component")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("name")?.Value,
                "Microsoft-Windows-Deployment",
                StringComparison.OrdinalIgnoreCase));
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
        int appliedImageIndex,
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
        if (!settings.IsEnabled || settings.Actions is null || settings.Actions.Count == 0)
        {
            return new WindowsOptionalFeatureServicingResult();
        }

        if (!WindowsOptionalFeatureActionValidator.TryNormalize(
            settings,
            out DeployWindowsOptionalFeatureSettings normalizedSettings,
            out string? validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        WindowsOptionalFeatureWorkItem[] requestedItems = normalizedSettings.Actions
            .Select(action =>
            {
                WindowsOptionalFeatureCatalogEntry entry = WindowsOptionalFeatureCatalog.Find(action.Id)!;
                return new WindowsOptionalFeatureWorkItem(action, entry, WindowsOptionalFeatureCatalog.GetDepth(entry.Id));
            })
            .ToArray();
        string cleanupRoot = Path.GetFullPath(Path.Combine(workingDirectory, ".."));
        if (Directory.GetParent(cleanupRoot) is null)
        {
            throw new ArgumentException("The optional-feature cleanup boundary cannot be a filesystem root.", nameof(workingDirectory));
        }

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
                TryCleanupOptionalFeatureDirectory(sourceExtractionDirectory, cleanupRoot);
                Directory.CreateDirectory(sourceExtractionDirectory);
                OptionalFeatureSourceMetadata metadata = await ResolveOptionalFeatureSourceMetadataAsync(
                        setupMediaImagePath,
                        appliedImageIndex,
                        workingDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                await _commands.RunRequiredProcessAsync(
                    "dism.exe",
                    [
                        "/English",
                        "/Apply-Image",
                        $"/ImageFile:{setupMediaImagePath}",
                        $"/Index:{metadata.SetupMediaIndex}",
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
                await _commands.RunRequiredProcessAsync(
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
    private async Task<IReadOnlyDictionary<string, OfflineWindowsFeatureState>> GetOfflineWindowsFeatureStatesAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _commands.RunRequiredProcessAsync(
            "dism.exe",
            [
                "/English",
                $"/Image:{windowsPartitionRoot}",
                "/Get-Features",
                "/Format:Table"
            ],
            workingDirectory,
            $"Failed to inspect Windows optional features in '{windowsPartitionRoot}'",
            cancellationToken, MetadataExecutionTimeout).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        IReadOnlyDictionary<string, OfflineWindowsFeatureState> states = ParseOfflineWindowsFeatureStates(result.StandardOutput);
        if (states.Count == 0)
        {
            throw new InvalidOperationException("Failed to parse Windows optional feature states from DISM output.");
        }

        return states;
    }

    private static IReadOnlyDictionary<string, OfflineWindowsFeatureState> ParseOfflineWindowsFeatureStates(string output)
    {
        var states = new Dictionary<string, OfflineWindowsFeatureState>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (output ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = line.IndexOf('|');
            if (separatorIndex < 0)
            {
                continue;
            }

            string name = line[..separatorIndex].Trim();
            string stateText = line[(separatorIndex + 1)..].Trim();
            if (name.Equals("Feature Name", StringComparison.OrdinalIgnoreCase) ||
                name.All(character => character is '-' or ' ') ||
                stateText.All(character => character is '-' or ' '))
            {
                continue;
            }

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

    private async Task<OptionalFeatureSourceMetadata> ResolveOptionalFeatureSourceMetadataAsync(
        string imagePath,
        int appliedImageIndex,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(appliedImageIndex, 1);

        ProcessExecutionResult summary = await _commands.RunRequiredProcessAsync(
            "dism.exe",
            ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}"],
            workingDirectory,
            $"Failed to inspect setup-media image '{imagePath}'",
            cancellationToken, MetadataExecutionTimeout).ConfigureAwait(false);
        summary.EnsureCompleteOutput();
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

        ProcessExecutionResult detail = await _commands.RunRequiredProcessAsync(
            "dism.exe",
            ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}", $"/Index:{appliedImageIndex}"],
            workingDirectory,
            $"Failed to inspect applied Windows image index {appliedImageIndex}",
            cancellationToken, MetadataExecutionTimeout).ConfigureAwait(false);
        detail.EnsureCompleteOutput();
        return new OptionalFeatureSourceMetadata(
            matches[0].Index,
            appliedImageIndex,
            ParseImageProperty(detail.StandardOutput, "Architecture"),
            ParseImageProperty(detail.StandardOutput, "Version"));
    }

    private static string ValidateMatchingNetFx3Source(
        string imagePath,
        string sourceExtractionDirectory,
        OptionalFeatureSourceMetadata metadata)
    {
        string sourcePath = Path.Combine(sourceExtractionDirectory, "sources", "sxs");
        string architectureToken = metadata.Architecture.ToUpperInvariant() switch
        {
            "X64" or "AMD64" => "amd64",
            "ARM64" => "arm64",
            _ => throw new InvalidOperationException(
                $"Applied Windows image index {metadata.AppliedImageIndex} in '{imagePath}' reports unsupported architecture '{metadata.Architecture}'.")
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

    private static string ParseImageProperty(string output, string propertyName)
    {
        Match match = Regex.Match(
            output,
            $@"^\s*{Regex.Escape(propertyName)}\s*:\s*(.+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private sealed record WindowsOptionalFeatureWorkItem(
        DeployWindowsOptionalFeatureAction Action,
        WindowsOptionalFeatureCatalogEntry CatalogEntry,
        int Depth);

    private sealed record OptionalFeatureSourceMetadata(
        int SetupMediaIndex,
        int AppliedImageIndex,
        string Architecture,
        string Version);

    private enum OfflineWindowsFeatureState
    {
        Enabled,
        Disabled,
        EnablePending,
        DisablePending,
        PayloadRemoved
    }
}

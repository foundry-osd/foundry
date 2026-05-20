using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;
using Microsoft.Win32;
using static Foundry.Deploy.Services.Deployment.Unattend.OfflineRegistryWriter;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Writes Windows AI component removal policies into offline target registry hives before first boot.
/// </summary>
internal sealed class AiComponentRemovalRegistryWriter
{
    private const string SoftwareHiveMount = @"HKLM\FoundrySoftware";
    private const string SystemHiveMount = @"HKLM\FoundrySystem";
    private const string DefaultUserHiveMount = @"HKU\FoundryDefault";
    private const string SystemHiveKeyName = "FoundrySystem";

    private readonly OfflineRegistryWriter _registryWriter;

    /// <summary>
    /// Initializes a registry writer that applies offline AI policy values with reg.exe.
    /// </summary>
    /// <param name="processRunner">The process runner used to load hives and write values.</param>
    public AiComponentRemovalRegistryWriter(IProcessRunner processRunner)
    {
        _registryWriter = new OfflineRegistryWriter(processRunner);
    }

    /// <summary>
    /// Applies selected AI removal policies to the offline Windows installation.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="settings">The AI component removal settings generated from the Foundry configuration.</param>
    /// <param name="workingDirectory">Directory used for temporary command output.</param>
    /// <param name="cancellationToken">Token that cancels registry configuration.</param>
    /// <returns>A task that completes after selected policy values are written.</returns>
    public async Task ApplyAsync(
        string windowsPartitionRoot,
        DeployAiComponentRemovalSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled || !HasAnyPolicyOptionEnabled(settings))
        {
            return;
        }

        string softwareHivePath = Path.Combine(windowsPartitionRoot, "Windows", "System32", "config", "SOFTWARE");
        string systemHivePath = Path.Combine(windowsPartitionRoot, "Windows", "System32", "config", "SYSTEM");
        string defaultUserHivePath = Path.Combine(windowsPartitionRoot, "Users", "Default", "NTUSER.DAT");

        if (RequiresSoftwareHive(settings))
        {
            await _registryWriter
                .WithLoadedHiveAsync(
                    SoftwareHiveMount,
                    softwareHivePath,
                    workingDirectory,
                    (hive, token) => ApplySoftwarePoliciesAsync(hive, settings, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (settings.DisableAiServiceAutoStart)
        {
            await _registryWriter
                .WithLoadedHiveAsync(
                    SystemHiveMount,
                    systemHivePath,
                    workingDirectory,
                    ApplySystemPoliciesAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (RequiresDefaultUserHive(settings))
        {
            await _registryWriter
                .WithLoadedHiveAsync(
                    DefaultUserHiveMount,
                    defaultUserHivePath,
                    workingDirectory,
                    (hive, token) => ApplyDefaultUserPoliciesAsync(hive, settings, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool HasAnyPolicyOptionEnabled(DeployAiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableAiServiceAutoStart ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }

    private static bool RequiresSoftwareHive(DeployAiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }

    private static bool RequiresDefaultUserHive(DeployAiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.DisableRecall ||
            settings.DisableClickToDo;
    }

    private async Task ApplySoftwarePoliciesAsync(
        OfflineRegistryHive hive,
        DeployAiComponentRemovalSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.RemoveCopilot)
        {
            await hive.AddDwordAsync(@"Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableRecall)
        {
            await hive.AddDwordAsync(@"Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(@"Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(@"Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableClickToDo)
        {
            await hive.AddDwordAsync(@"Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableEdgeAi)
        {
            const string edgePolicyPath = @"Policies\Microsoft\Edge";
            await hive.AddDwordAsync(edgePolicyPath, "CopilotCDPPageContext", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "CopilotPageContext", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "HubsSidebarEnabled", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "EdgeEntraCopilotPageContext", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "EdgeHistoryAISearchEnabled", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "ComposeInlineEnabled", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "GenAILocalFoundationalModelSettings", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(edgePolicyPath, "NewTabPageBingChatEnabled", 0, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisablePaintAi)
        {
            const string paintPolicyPath = @"Microsoft\Windows\CurrentVersion\Policies\Paint";
            await hive.AddDwordAsync(paintPolicyPath, "DisableCocreator", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(paintPolicyPath, "DisableGenerativeFill", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(paintPolicyPath, "DisableImageCreator", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(paintPolicyPath, "DisableGenerativeErase", 1, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(paintPolicyPath, "DisableRemoveBackground", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableNotepadAi)
        {
            await hive.AddDwordAsync(@"Policies\WindowsNotepad", "DisableAIFeatures", 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task ApplySystemPoliciesAsync(
        OfflineRegistryHive hive,
        CancellationToken cancellationToken)
    {
        string controlSetName = ResolveCurrentControlSetName();
        return hive.AddDwordAsync(
            $@"{controlSetName}\Services\WSAIFabricSvc",
            "Start",
            3,
            cancellationToken);
    }

    private async Task ApplyDefaultUserPoliciesAsync(
        OfflineRegistryHive hive,
        DeployAiComponentRemovalSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.RemoveCopilot)
        {
            await hive.AddDwordAsync(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, cancellationToken).ConfigureAwait(false);
            await hive.AddDwordAsync(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableRecall)
        {
            await hive.AddDwordAsync(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableClickToDo)
        {
            await hive.AddDwordAsync(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveCurrentControlSetName()
    {
        try
        {
            using RegistryKey? selectKey = Registry.LocalMachine.OpenSubKey($@"{SystemHiveKeyName}\Select");
            if (selectKey?.GetValue("Current") is int currentSet && currentSet > 0)
            {
                return $"ControlSet{currentSet:D3}";
            }
        }
        catch
        {
            // Fall through to the Windows default control set.
        }

        return "ControlSet001";
    }
}

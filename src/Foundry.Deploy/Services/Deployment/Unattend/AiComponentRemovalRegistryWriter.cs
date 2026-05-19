using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;
using Microsoft.Win32;

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

    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a registry writer that applies offline AI policy values with reg.exe.
    /// </summary>
    /// <param name="processRunner">The process runner used to load hives and write values.</param>
    public AiComponentRemovalRegistryWriter(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
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
            await LoadHiveAsync(SoftwareHiveMount, softwareHivePath, workingDirectory, cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplySoftwarePoliciesAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await UnloadHiveAsync(SoftwareHiveMount, workingDirectory, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (settings.DisableAiServiceAutoStart)
        {
            await LoadHiveAsync(SystemHiveMount, systemHivePath, workingDirectory, cancellationToken).ConfigureAwait(false);
            try
            {
                string controlSetName = ResolveCurrentControlSetName();
                await AddDwordAsync(
                    $@"{SystemHiveMount}\{controlSetName}\Services\WSAIFabricSvc",
                    "Start",
                    3,
                    workingDirectory,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await UnloadHiveAsync(SystemHiveMount, workingDirectory, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (RequiresDefaultUserHive(settings))
        {
            await LoadHiveAsync(DefaultUserHiveMount, defaultUserHivePath, workingDirectory, cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyDefaultUserPoliciesAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await UnloadHiveAsync(DefaultUserHiveMount, workingDirectory, CancellationToken.None).ConfigureAwait(false);
            }
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
        DeployAiComponentRemovalSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (settings.RemoveCopilot)
        {
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableRecall)
        {
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableClickToDo)
        {
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableEdgeAi)
        {
            string edgePolicyPath = $@"{SoftwareHiveMount}\Policies\Microsoft\Edge";
            await AddDwordAsync(edgePolicyPath, "CopilotCDPPageContext", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "CopilotPageContext", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "HubsSidebarEnabled", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "EdgeEntraCopilotPageContext", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "EdgeHistoryAISearchEnabled", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "ComposeInlineEnabled", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "GenAILocalFoundationalModelSettings", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(edgePolicyPath, "NewTabPageBingChatEnabled", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisablePaintAi)
        {
            string paintPolicyPath = $@"{SoftwareHiveMount}\Microsoft\Windows\CurrentVersion\Policies\Paint";
            await AddDwordAsync(paintPolicyPath, "DisableCocreator", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(paintPolicyPath, "DisableGenerativeFill", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(paintPolicyPath, "DisableImageCreator", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(paintPolicyPath, "DisableGenerativeErase", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync(paintPolicyPath, "DisableRemoveBackground", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableNotepadAi)
        {
            await AddDwordAsync($@"{SoftwareHiveMount}\Policies\WindowsNotepad", "DisableAIFeatures", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyDefaultUserPoliciesAsync(
        DeployAiComponentRemovalSettings settings,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (settings.RemoveCopilot)
        {
            await AddDwordAsync($@"{DefaultUserHiveMount}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, workingDirectory, cancellationToken).ConfigureAwait(false);
            await AddDwordAsync($@"{DefaultUserHiveMount}\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableRecall)
        {
            await AddDwordAsync($@"{DefaultUserHiveMount}\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DisableClickToDo)
        {
            await AddDwordAsync($@"{DefaultUserHiveMount}\Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, workingDirectory, cancellationToken).ConfigureAwait(false);
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

    private Task LoadHiveAsync(string mountName, string hivePath, string workingDirectory, CancellationToken cancellationToken)
    {
        return RunRequiredAsync("reg.exe", ["LOAD", mountName, hivePath], workingDirectory, cancellationToken);
    }

    private Task UnloadHiveAsync(string mountName, string workingDirectory, CancellationToken cancellationToken)
    {
        return RunRequiredAsync("reg.exe", ["UNLOAD", mountName], workingDirectory, cancellationToken);
    }

    private Task AddDwordAsync(
        string keyPath,
        string valueName,
        int value,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RunRequiredAsync(
            "reg.exe",
            ["ADD", keyPath, "/v", valueName, "/t", "REG_DWORD", "/d", value.ToString(), "/f"],
            workingDirectory,
            cancellationToken);
    }

    private async Task RunRequiredAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }
    }
}

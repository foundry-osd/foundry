using System.Globalization;
using System.IO;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.WinPe.OsRecovery;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.System;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ProvisionOsRecoveryStep : DeploymentStepBase
{
    private const string ConnectConfigurationPath = @"X:\Foundry\Config\foundry.connect.config.json";
    private const string WinReImageFileName = "winre.wim";
    private const string RecoveryMarkerFileName = "FoundryOsRecovery.json";

    private readonly IOsRecoveryPayloadProvisioningService _osRecoveryPayloadProvisioningService;
    private readonly IWinPeEmbeddedAssetService _embeddedAssetService;
    private readonly IDeployConfigurationService _deployConfigurationService;
    private readonly IProcessRunner _processRunner;
    private readonly string? _winReConfigToolPath;

    public ProvisionOsRecoveryStep(
        IOsRecoveryPayloadProvisioningService osRecoveryPayloadProvisioningService,
        IWinPeEmbeddedAssetService embeddedAssetService,
        IDeployConfigurationService deployConfigurationService,
        IProcessRunner processRunner)
        : this(
            osRecoveryPayloadProvisioningService,
            embeddedAssetService,
            deployConfigurationService,
            processRunner,
            winReConfigToolPath: null)
    {
    }

    internal ProvisionOsRecoveryStep(
        IOsRecoveryPayloadProvisioningService osRecoveryPayloadProvisioningService,
        IWinPeEmbeddedAssetService embeddedAssetService,
        IDeployConfigurationService deployConfigurationService,
        IProcessRunner processRunner,
        string? winReConfigToolPath)
    {
        _osRecoveryPayloadProvisioningService = osRecoveryPayloadProvisioningService;
        _embeddedAssetService = embeddedAssetService;
        _deployConfigurationService = deployConfigurationService;
        _processRunner = processRunner;
        _winReConfigToolPath = winReConfigToolPath;
    }

    public override int Order => 15;

    public override string Name => DeploymentStepNames.ProvisionOsRecovery;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.OsRecovery.IsEnabled)
        {
            return DeploymentStepResult.Skipped("OS Recovery is disabled.");
        }

        if (context.RuntimeState.Mode == DeploymentMode.Recovery)
        {
            return DeploymentStepResult.Skipped("OS Recovery provisioning is skipped in recovery mode.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot) ||
            !context.RuntimeState.WinReConfigured)
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment", "OsRecovery");
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        string mountPath = Path.Combine(workingDirectory, "Mount-WindowsRE");
        string winReImagePath = ResolveWinReImagePath(context.RuntimeState.TargetRecoveryPartitionRoot);
        string recoveryMarkerPath = ResolveRecoveryMarkerPath(context.RuntimeState.TargetRecoveryPartitionRoot);
        string windowsPath = Path.Combine(context.RuntimeState.TargetWindowsPartitionRoot, "Windows");

        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(scratchDirectory);
        ResetDirectory(mountPath);

        string deployConfigurationJson = await ReadDeployConfigurationJsonAsync(cancellationToken).ConfigureAwait(false);
        string connectConfigurationJson = await ReadRecoveryConnectConfigurationJsonAsync(cancellationToken).ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Provisioning OS Recovery...", "Mounting WinRE...");
        await RunRequiredProcessAsync(
            "dism.exe",
            [
                "/Mount-Image",
                $"/ImageFile:{winReImagePath}",
                "/Index:1",
                $"/MountDir:{mountPath}",
                $"/ScratchDir:{scratchDirectory}"
            ],
            workingDirectory,
            "Failed to mount the Windows RE image",
            cancellationToken).ConfigureAwait(false);

        bool mounted = true;
        bool shouldCommit = false;
        bool markerWritten = false;
        Exception? operationException = null;
        try
        {
            context.EmitCurrentStepIndeterminate("Provisioning OS Recovery...", "Injecting OS Recovery payload...");
            WinPeResult<OsRecoveryPayloadProvisioningResult> provisioningResult =
                await _osRecoveryPayloadProvisioningService.ProvisionAsync(
                    new OsRecoveryPayloadProvisioningOptions
                    {
                        MountedImagePath = mountPath,
                        WorkingDirectoryPath = Path.Combine(workingDirectory, "Payload"),
                        Architecture = ResolveArchitecture(context.Request.OperatingSystem.Architecture),
                        FoundryConnectConfigurationJson = connectConfigurationJson,
                        DeployConfigurationJson = deployConfigurationJson,
                        IanaWindowsTimeZoneMapJson = _embeddedAssetService.GetIanaWindowsTimeZoneMapJson(),
                        CurlExecutableSourcePath = ResolveCurlExecutablePath(),
                        SevenZipSourceDirectoryPath = _embeddedAssetService.GetSevenZipSourceDirectoryPath(),
                        Connect = new WinPeRuntimePayloadApplicationOptions
                        {
                            IsEnabled = true,
                            ProvisioningSource = WinPeProvisioningSource.Release
                        },
                        BootMenuLocalizations = CreateBootMenuLocalizations()
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!provisioningResult.IsSuccess)
            {
                throw new InvalidOperationException(FormatDiagnostic(provisioningResult.Error));
            }

            shouldCommit = true;

            context.EmitCurrentStepIndeterminate("Provisioning OS Recovery...", "Committing WinRE changes...");
            await UnmountAsync(mountPath, workingDirectory, shouldCommit: true, cancellationToken).ConfigureAwait(false);
            mounted = false;

            context.EmitCurrentStepIndeterminate("Provisioning OS Recovery...", "Registering OS Recovery boot menu...");
            string bootMenuConfigPath = Path.Combine(workingDirectory, "AddFoundryRecoveryToBootMenu.xml");
            await File.WriteAllTextAsync(
                bootMenuConfigPath,
                provisioningResult.Value!.BootMenuConfigurationXml,
                cancellationToken).ConfigureAwait(false);

            await WriteRecoveryMarkerAsync(recoveryMarkerPath, context, provisioningResult.Value, cancellationToken).ConfigureAwait(false);
            markerWritten = true;

            await RunRequiredProcessAsync(
                ResolveRequiredWinReConfigToolPath(),
                ["/setbootshelllink", "/configfile", bootMenuConfigPath, "/target", windowsPath],
                workingDirectory,
                "Failed to register the OS Recovery boot menu link",
                cancellationToken).ConfigureAwait(false);

            await context.AppendLogAsync(
                DeploymentLogLevel.Info,
                $"OS Recovery provisioned. ManagedPayloadSizeBytes={provisioningResult.Value.ManagedPayloadSizeBytes}.",
                cancellationToken).ConfigureAwait(false);

            return DeploymentStepResult.Succeeded("OS Recovery provisioned.");
        }
        catch (Exception ex)
        {
            operationException = ex;
            if (markerWritten)
            {
                TryDeleteFile(recoveryMarkerPath);
            }

            throw;
        }
        finally
        {
            if (mounted)
            {
                try
                {
                    await UnmountAsync(mountPath, workingDirectory, shouldCommit, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception unmountException) when (operationException is not null)
                {
                    await TryAppendCleanupFailureLogAsync(context, unmountException).ConfigureAwait(false);
                }
            }

            TryDeleteDirectory(mountPath);
        }
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.OsRecovery.IsEnabled)
        {
            return DeploymentStepResult.Skipped("OS Recovery is disabled.");
        }

        if (context.RuntimeState.Mode == DeploymentMode.Recovery)
        {
            return DeploymentStepResult.Skipped("OS Recovery provisioning is skipped in recovery mode.");
        }

        context.EmitCurrentStepIndeterminate("Provisioning OS Recovery...", "Injecting OS Recovery payload...");
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "[DRY-RUN] Simulated OS Recovery WinRE payload provisioning.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("OS Recovery provisioned (simulation).");
    }

    private async Task<string> ReadDeployConfigurationJsonAsync(CancellationToken cancellationToken)
    {
        DeployConfigurationLoadResult loadResult = _deployConfigurationService.LoadOptional();
        if (!loadResult.Exists || string.IsNullOrWhiteSpace(loadResult.ConfigurationPath) || !File.Exists(loadResult.ConfigurationPath))
        {
            throw new FileNotFoundException("The Foundry.Deploy configuration file is required for OS Recovery provisioning.", loadResult.ConfigurationPath);
        }

        await using FileStream stream = File.OpenRead(loadResult.ConfigurationPath);
        FoundryDeployConfigurationDocument? document = await JsonSerializer.DeserializeAsync<FoundryDeployConfigurationDocument>(
            stream,
            ConfigurationJsonDefaults.SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            throw new InvalidOperationException("The Foundry.Deploy configuration file could not be parsed for OS Recovery provisioning.");
        }

        FoundryDeployConfigurationDocument recoveryDocument = document with
        {
            Autopilot = new DeployAutopilotSettings(),
            Network = new CoreDeployNetworkSettings()
        };

        return JsonSerializer.Serialize(recoveryDocument, ConfigurationJsonDefaults.SerializerOptions);
    }

    private static async Task<string> ReadRecoveryConnectConfigurationJsonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConnectConfigurationPath))
        {
            return JsonSerializer.Serialize(new FoundryConnectConfigurationDocument(), ConfigurationJsonDefaults.SerializerOptions);
        }

        await using FileStream stream = File.OpenRead(ConnectConfigurationPath);
        FoundryConnectConfigurationDocument? document = await JsonSerializer.DeserializeAsync<FoundryConnectConfigurationDocument>(
            stream,
            ConfigurationJsonDefaults.SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        document ??= new FoundryConnectConfigurationDocument();
        FoundryConnectConfigurationDocument recoveryDocument = new()
        {
            SchemaVersion = document.SchemaVersion,
            InternetProbe = document.InternetProbe,
            Telemetry = document.Telemetry
        };

        return JsonSerializer.Serialize(recoveryDocument, ConfigurationJsonDefaults.SerializerOptions);
    }

    private static IReadOnlyList<OsRecoveryBootMenuLocalization> CreateBootMenuLocalizations()
    {
        return new EmbeddedLanguageRegistryService()
            .GetLanguages()
            .Select(language =>
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(language.Code);
                return new OsRecoveryBootMenuLocalization
                {
                    Culture = language.Code,
                    Name = LocalizationText.ResourceManager.GetString("OsRecovery.BootMenuName", culture) ?? "Foundry Recovery",
                    Description = LocalizationText.ResourceManager.GetString("OsRecovery.BootMenuDescription", culture) ?? "Redeploy Windows"
                };
            })
            .ToArray();
    }

    private async Task UnmountAsync(
        string mountPath,
        string workingDirectory,
        bool shouldCommit,
        CancellationToken cancellationToken)
    {
        await RunRequiredProcessAsync(
            "dism.exe",
            shouldCommit
                ? ["/Unmount-Image", $"/MountDir:{mountPath}", "/Commit"]
                : ["/Unmount-Image", $"/MountDir:{mountPath}", "/Discard"],
            workingDirectory,
            shouldCommit
                ? "Failed to commit the Windows RE image"
                : "Failed to discard the Windows RE image",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryAppendCleanupFailureLogAsync(
        DeploymentStepExecutionContext context,
        Exception exception)
    {
        try
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Warning,
                $"Failed to clean up mounted WinRE image after OS Recovery provisioning failure. {exception.Message}",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task RunRequiredProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync(fileName, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return;
        }

        throw new InvalidOperationException($"{failureMessage}.{Environment.NewLine}{ToDiagnostic(result)}");
    }

    private static WinPeArchitecture ResolveArchitecture(string? architecture)
    {
        return !string.IsNullOrWhiteSpace(architecture) &&
               architecture.Contains("arm", StringComparison.OrdinalIgnoreCase)
            ? WinPeArchitecture.Arm64
            : WinPeArchitecture.X64;
    }

    private static string ResolveCurlExecutablePath()
    {
        return Path.Combine(Environment.SystemDirectory, "curl.exe");
    }

    private static string ResolveWinReImagePath(string recoveryPartitionRoot)
    {
        return Path.Combine(recoveryPartitionRoot, "Recovery", "WindowsRE", WinReImageFileName);
    }

    private static string ResolveRecoveryMarkerPath(string recoveryPartitionRoot)
    {
        return Path.Combine(recoveryPartitionRoot, "Recovery", "WindowsRE", RecoveryMarkerFileName);
    }

    private static Task WriteRecoveryMarkerAsync(
        string markerPath,
        DeploymentStepExecutionContext context,
        OsRecoveryPayloadProvisioningResult provisioningResult,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var marker = new
        {
            schemaVersion = 1,
            product = "Foundry OS Recovery",
            createdUtc = DateTimeOffset.UtcNow,
            targetDiskNumber = context.RuntimeState.TargetDiskNumber,
            managedPayloadSizeBytes = provisioningResult.ManagedPayloadSizeBytes
        };

        string json = JsonSerializer.Serialize(marker, ConfigurationJsonDefaults.SerializerOptions);
        return File.WriteAllTextAsync(markerPath, json, cancellationToken);
    }

    private string ResolveRequiredWinReConfigToolPath()
    {
        if (!string.IsNullOrWhiteSpace(_winReConfigToolPath))
        {
            return _winReConfigToolPath;
        }

        string path = Path.Combine(Environment.SystemDirectory, "winrecfg.exe");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Required WinPE executable 'winrecfg.exe' was not found. Add the WinPE-WinReCfg optional component to the WinPE image.",
                path);
        }

        return path;
    }

    private static void ResetDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string FormatDiagnostic(WinPeDiagnostic? diagnostic)
    {
        if (diagnostic is null)
        {
            return "OS Recovery payload provisioning failed.";
        }

        return string.IsNullOrWhiteSpace(diagnostic.Details)
            ? diagnostic.Message
            : $"{diagnostic.Message}{Environment.NewLine}{diagnostic.Details}";
    }

    private static string ToDiagnostic(ProcessExecutionResult result)
    {
        string stdout = string.IsNullOrWhiteSpace(result.StandardOutput) ? string.Empty : result.StandardOutput.Trim();
        string stderr = string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : result.StandardError.Trim();

        return string.Join(
            Environment.NewLine,
            [
                $"Command: {result.FileName} {result.Arguments}",
                $"ExitCode: {result.ExitCode}",
                stdout.Length == 0 ? "StdOut: <empty>" : $"StdOut: {stdout}",
                stderr.Length == 0 ? "StdErr: <empty>" : $"StdErr: {stderr}"
            ]);
    }
}

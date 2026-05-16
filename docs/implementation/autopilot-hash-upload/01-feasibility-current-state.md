# Autopilot Hardware Hash Upload - Feasibility And Current State

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

## Feasibility Summary
- WinPE hardware hash capture is technically feasible with `OA3Tool.exe /Report`.
- This is an operational workaround, not Microsoft's standard recommended Autopilot registration path.
- The current Foundry architecture already has the right integration boundaries:
  - Foundry OSD configures and builds WinPE media.
  - Foundry Deploy runs inside WinPE and already stages the selected JSON profile into the applied Windows image.
  - Foundry Connect is not part of the feature scope.
- The final implementation should support x64 and ARM64 by using architecture-specific ADK assets.
- The final implementation should support available WinPE networking, including Ethernet and Wi-Fi, and should avoid destructive cleanup of existing Intune, Autopilot, or Entra records.
- Hash capture and upload should run near the end of the OS deployment workflow, after the Windows image is applied and the target Windows `System32` directory is available.


## Feasibility Constraints
WinPE capture is useful because it lets an operator register a device before the installed OS reaches OOBE. The tradeoff is that Microsoft documents the normal Autopilot hash capture path around a full Windows environment, OOBE diagnostics, Audit Mode, or OEM/reseller registration.

Operational constraints:
- Ethernet and Wi-Fi should be treated as equivalent supported network paths when WinPE has the required network stack, drivers, and credentials.
- Network readiness validation should focus on actual connectivity to Microsoft Graph, not on the adapter type.
- TPM-dependent scenarios require extra caution. Self-deploying and pre-provisioning depend heavily on correct TPM capture.
- Drivers matter. Missing storage, chipset, NIC, or TPM visibility can produce an empty or incomplete hash.
- ADK version and architecture matter. The OA3Tool staged into media should come from the same installed ADK family used to build the WinPE image, with separate x64 and ARM64 resolution.
- `PCPKsp.dll` should not be bundled into media at build time. Foundry Deploy should copy it from `<target Windows>\Windows\System32\PCPKsp.dll` to `X:\Windows\System32\PCPKsp.dll` after the OS image has been applied.

Support positioning:
- Supported by Foundry as a best-effort workflow for user-driven Autopilot registration.
- Not positioned as the Microsoft-standard or OEM-supported registration workflow.
- Not recommended for self-deploying or pre-provisioning until physical validation proves TPM/EKPub capture quality.


## Current State
Foundry currently supports one Autopilot provisioning method:

```text
Foundry OSD -> WinPE media config -> Foundry Deploy -> Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json
```

Key files:
- `src/Foundry/Views/AutopilotPage.xaml`
- `src/Foundry/ViewModels/AutopilotConfigurationViewModel.cs`
- `src/Foundry.Core/Models/Configuration/AutopilotSettings.cs`
- `src/Foundry.Core/Models/Configuration/Deploy/DeployAutopilotSettings.cs`
- `src/Foundry.Core/Services/Configuration/DeployConfigurationGenerator.cs`
- `src/Foundry.Core/Services/WinPe/WinPeMountedImageAssetProvisioningService.cs`
- `src/Foundry.Deploy/Services/Autopilot/AutopilotProfileCatalogService.cs`
- `src/Foundry.Deploy/Services/Deployment/DeploymentLaunchPreparationService.cs`
- `src/Foundry.Deploy/Services/Deployment/Steps/StageAutopilotConfigurationStep.cs`

Current data flow:

```text
Foundry app
  -> ExpertDeployConfigurationStateService
  -> DeployConfigurationGenerator
  -> StartMediaViewModel
  -> WinPeWorkspacePreparationService
  -> WinPeMountedImageAssetProvisioningService
  -> X:\Foundry\Config\foundry.deploy.config.json
  -> X:\Foundry\Config\Autopilot\<profile>\AutopilotConfigurationFile.json
  -> Foundry.Deploy startup
  -> AutopilotProfileCatalogService
  -> DeploymentLaunchPreparationService
  -> StageAutopilotConfigurationStep
  -> <offline Windows>\Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json
```

Target hash upload data flow:

```text
Foundry Deploy deployment run
  -> apply Windows image
  -> configure target computer name, recovery, drivers, firmware, and partition sealing
  -> enter the late Autopilot provisioning step
  -> resolve the target Windows root
  -> copy <target Windows>\Windows\System32\PCPKsp.dll to X:\Windows\System32\PCPKsp.dll
  -> run C# OA3Tool capture service
  -> parse OA3.xml and create diagnostic CSV
  -> run C# Graph import service
  -> poll import state when configured
  -> retain OA3, CSV, and upload result logs under the applied OS
  -> finalize deployment
```

Current assumptions to break carefully:
- `IsAutopilotEnabled` currently implies "a JSON profile must be selected".
- `AutopilotProfileCatalogService` only loads folder-based JSON profile assets.
- `StageAutopilotConfigurationStep` has a profile-staging name and should become the late, mode-aware Autopilot provisioning boundary.
- Media readiness only checks whether a selected profile exists when Autopilot is enabled.
- Deploy summary and telemetry only track `autopilot_enabled`, not the provisioning method.



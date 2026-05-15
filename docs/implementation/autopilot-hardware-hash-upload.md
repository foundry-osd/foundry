# Autopilot Hardware Hash Upload Implementation Plan

## Purpose
This document defines the feasibility, integration approach, and phased implementation plan for adding Windows Autopilot hardware hash capture and upload from WinPE to Foundry.

This feature is intended to complement the existing offline Autopilot JSON profile staging flow. It must not replace the current behavior.

## Branch Strategy
- Foundation branch: `feature/autopilot-hash-upload-foundation`
- Foundation worktree: `C:\Users\mchav\.config\superpowers\worktrees\foundry\autopilot-hash-upload-foundation`
- Phase branches should branch from the foundation branch:
  - `feature/autopilot-hash-upload-config`
  - `feature/autopilot-hash-upload-media`
  - `feature/autopilot-hash-upload-runtime`
  - `feature/autopilot-hash-upload-graph`
  - `feature/autopilot-hash-upload-docs`

The foundation branch should remain documentation-first. Implementation branches should be small, reviewable, and merged back into the foundation branch in phase order.

## Feasibility Summary
- WinPE hardware hash capture is technically feasible with `OA3Tool.exe /Report`.
- This is an operational workaround, not Microsoft's standard recommended Autopilot registration path.
- The current Foundry architecture already has the right integration boundaries:
  - Foundry OSD configures and builds WinPE media.
  - Foundry Deploy runs inside WinPE and already stages the selected JSON profile into the applied Windows image.
  - Foundry Connect is not part of the feature scope.
- V1 should target x64, Ethernet-first, first-time import scenarios.
- V1 should avoid destructive cleanup of existing Intune, Autopilot, or Entra records.

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

## Target UX
On the Autopilot page:

- The user enables or disables Autopilot globally.
- When enabled, two settings expanders are shown:
  - Offline JSON profile provisioning.
  - Hardware hash capture and upload.
- Only one provisioning method can be active at a time.
- Existing JSON import, tenant profile download, default profile selection, and profile table remain inside the JSON provisioning expander.
- Hardware hash upload settings stay in a separate expander with its own validation and readiness text.

## Proposed Runtime Model
Add an explicit provisioning mode.

```csharp
public enum AutopilotProvisioningMode
{
    JsonProfile,
    HardwareHashUpload
}
```

Expert configuration should persist:
- `IsEnabled`
- `ProvisioningMode`
- existing JSON profile properties
- hardware hash upload settings

Deploy runtime configuration should receive only the reduced settings needed by WinPE:
- `IsEnabled`
- `ProvisioningMode`
- selected JSON profile folder name when in JSON mode
- hash upload configuration when in hash mode

Existing persisted configurations must continue to behave as JSON profile mode.

## Authentication Recommendation
V1 should not install PowerShell modules inside WinPE.

Recommended direction:
- Use direct Microsoft Graph REST calls or a small .NET service abstraction.
- Use least-privilege upload permissions:
  - `DeviceManagementServiceConfig.ReadWrite.All` for import.
  - `DeviceManagementServiceConfig.Read.All` only if read-only polling is separated.
- Defer destructive permissions:
  - `DeviceManagementManagedDevices.ReadWrite.All`
  - `Device.ReadWrite.All`
  - `GroupMember.ReadWrite.All`

Authentication options to evaluate during implementation:
- Device code flow for operator-driven upload.
- Certificate-based app-only auth for controlled lab or factory use.
- A brokered upload workflow outside WinPE if storing credentials in media is rejected.

Private keys, client secrets, and tenant-wide destructive permissions must not be silently embedded into generated media.

## Microsoft Graph Import Shape
Use Microsoft Graph `v1.0`:

```http
POST /deviceManagement/importedWindowsAutopilotDeviceIdentities/import
```

Device payload fields:
- `serialNumber`
- `hardwareIdentifier`
- `groupTag`
- `assignedUserPrincipalName`
- `importId`

Import state polling should handle:
- `unknown`
- `pending`
- `partial`
- `complete`
- `error`

## WinPE Hash Capture Strategy
V1 capture path:

1. Stage `oa3tool.exe` from the local ADK.
2. Generate an OA3 config file and dummy input key file.
3. Run:

```cmd
oa3tool.exe /Report /ConfigFile=.\OA3.cfg /NoKeyCheck /LogTrace=.\OA3.log
```

4. Read `OA3.xml`.
5. Extract:
   - hardware hash
   - serial number
6. Save diagnostics:
   - `OA3.xml`
   - `OA3.log`
   - generated CSV
   - Foundry upload result JSON

V1 should add or gate `WinPE-SecureStartup` because TPM visibility matters for Autopilot quality. Existing media already includes WMI, NetFX, Scripting, PowerShell, WinReCfg, DismCmdlets, StorageWMI, Dot3Svc, and EnhancedStorage.

`PCPKsp.dll` must be treated as an unresolved legal/support item. Do not bundle it in OSS releases until redistribution and support constraints are validated.

## Phased Implementation

### Phase 0: Foundation Branch And Research
- [x] Create dedicated worktree.
- [x] Create foundation branch.
- [x] Analyze supplied feasibility document.
- [x] Analyze supplied WinPE upload script.
- [x] Query current Microsoft Graph and MSAL documentation through Context7.
- [x] Run baseline tests.

Manual checks:
- [x] Confirm branch is isolated from `main`.
- [x] Confirm baseline test command is known: `dotnet test .\src\Foundry.slnx --configuration Debug /p:Platform=x64`.

### Phase 1: Configuration Model
- [ ] Add `AutopilotProvisioningMode`.
- [ ] Extend `AutopilotSettings` with mode and hardware hash upload settings.
- [ ] Extend `DeployAutopilotSettings` with reduced runtime mode and upload settings.
- [ ] Update schema version handling if needed.
- [ ] Keep old configurations backward compatible as JSON profile mode.
- [ ] Update sanitization in `ExpertDeployConfigurationStateService`.
- [ ] Update `DeployConfigurationGenerator`.

Automated tests:
- [ ] Existing JSON profile config serializes and generates the same deploy output.
- [ ] Enabled JSON mode requires a selected profile.
- [ ] Enabled hash upload mode does not require a selected profile.
- [ ] Invalid hash upload settings make Autopilot configuration not ready.

Manual checks:
- [ ] Start Foundry with existing user config and confirm JSON profile mode is selected.
- [ ] Disable Autopilot and confirm no profile or hash settings are required.

### Phase 2: Autopilot Page UX
- [ ] Replace single Autopilot action section with two settings expanders.
- [ ] Keep global Autopilot toggle.
- [ ] Move existing import/download/remove/default profile/table UI into JSON profile expander.
- [ ] Add hardware hash upload expander.
- [ ] Enforce mutual exclusivity between JSON profile and hash upload modes.
- [ ] Add localized strings in English and French resources.
- [ ] Update readiness messages to include selected mode.

Automated tests:
- [ ] View model mode changes save state.
- [ ] Selecting JSON mode disables hash upload readiness requirements.
- [ ] Selecting hash upload mode disables JSON profile selection requirements.
- [ ] Busy state still blocks JSON profile import/download/remove commands.

Manual checks:
- [ ] Autopilot disabled: both expanders are unavailable or collapsed according to final UX decision.
- [ ] Autopilot enabled: both expanders are visible.
- [ ] Activating one method deactivates the other.
- [ ] JSON profile import and tenant download still work.

### Phase 3: Media Build And WinPE Assets
- [ ] Add `WinPE-SecureStartup` to required optional components or gate it behind hash upload mode.
- [ ] Locate and stage `oa3tool.exe` from the ADK.
- [ ] Add hash capture templates under a Foundry-owned WinPE path.
- [ ] Add hash upload runtime configuration under `X:\Foundry\Config`.
- [ ] Keep current profile JSON staging unchanged in JSON profile mode.
- [ ] Do not stage JSON profile folders in hash upload mode unless the user also keeps profiles for another purpose.

Automated tests:
- [ ] Media asset provisioning writes JSON profile assets only in JSON mode.
- [ ] Media asset provisioning writes hash upload assets only in hash mode.
- [ ] Missing `oa3tool.exe` produces a clear validation error.
- [ ] `WinPE-SecureStartup` missing or not applicable is surfaced clearly.

Manual checks:
- [ ] Build x64 ISO in JSON profile mode and confirm existing profile files are present.
- [ ] Build x64 ISO in hash upload mode and confirm OA3/hash assets are present.
- [ ] Confirm `WinPE-SecureStartup` is present in the mounted image package list.
- [ ] Confirm no private key or client secret is written to media without explicit user action.

### Phase 4: Foundry Deploy Runtime Branching
- [ ] Load Autopilot provisioning mode from deploy config.
- [ ] Expose mode in startup snapshot, preparation view model, launch request, deployment context, and runtime state.
- [ ] Update `DeploymentLaunchPreparationService` validation:
  - JSON mode requires selected profile.
  - Hash upload mode requires valid upload settings.
- [ ] Split current `StageAutopilotConfigurationStep` behavior:
  - JSON mode copies `AutopilotConfigurationFile.json`.
  - Hash upload mode runs the hash capture/upload workflow.
- [ ] Update deployment summary, logs, and telemetry with mode.

Automated tests:
- [ ] JSON mode still stages the profile to `Windows\Provisioning\Autopilot`.
- [ ] Hash upload mode skips JSON staging.
- [ ] Dry run creates a hash-mode manifest without touching Graph.
- [ ] Launch preparation rejects incomplete hash upload settings.

Manual checks:
- [ ] Deploy dry-run in JSON mode.
- [ ] Deploy dry-run in hash upload mode.
- [ ] Confirm summary page displays the selected Autopilot method.
- [ ] Confirm logs contain mode, hash capture diagnostics path, and upload state.

### Phase 5: Hash Capture Service
- [ ] Add a service that runs OA3Tool with controlled working directory paths.
- [ ] Generate `OA3.cfg` and dummy input XML internally.
- [ ] Validate `OA3.xml` exists.
- [ ] Extract serial number and hardware hash.
- [ ] Write a local CSV artifact for troubleshooting.
- [ ] Preserve OA3 logs in Foundry deployment logs.
- [ ] Return structured failure codes for missing tool, empty hash, invalid XML, missing serial, and OA3 exit failure.

Automated tests:
- [ ] Parses valid `OA3.xml`.
- [ ] Rejects missing `HardwareHash`.
- [ ] Rejects invalid XML.
- [ ] Generates CSV without quotes, extra columns, or Unicode encoding.
- [ ] Sanitizes commas from group tag and serial number.

Manual checks:
- [ ] Run on one x64 physical test device with Ethernet.
- [ ] Confirm generated hash imports manually in Intune.
- [ ] Confirm troubleshooting files are retained in logs.

### Phase 6: Graph Upload Service
- [ ] Add a minimal Graph Autopilot import client.
- [ ] Implement import request.
- [ ] Implement polling for import completion.
- [ ] Map Graph errors to operator-readable messages.
- [ ] Add retry/backoff for transient HTTP failures.
- [ ] Keep destructive cleanup out of V1.

Automated tests:
- [ ] Serializes import payload correctly.
- [ ] Sends hardware identifier in the expected Graph format.
- [ ] Handles `complete`.
- [ ] Handles `error` with device error code/name.
- [ ] Times out with a clear message.
- [ ] Retries transient failures only.

Manual checks:
- [ ] Import one test device into a test tenant.
- [ ] Confirm Group Tag appears in Intune.
- [ ] Confirm assignment sync behavior is documented, even if not waited on in V1.
- [ ] Confirm duplicate device behavior is clear to the operator.

### Phase 7: Security And Tenant Onboarding
- [ ] Add a permission matrix to user documentation.
- [ ] Add tenant/app registration guidance.
- [ ] Decide supported auth mode for V1.
- [ ] Validate whether certificate auth can be safely used from generated media.
- [ ] Explicitly document unsupported secret embedding patterns.
- [ ] Add audit-safe logging rules.

Automated tests:
- [ ] Secret settings are not serialized into plain deploy config unless intentionally allowed.
- [ ] Logs redact tokens, secrets, private key paths, and certificate material.

Manual checks:
- [ ] Review generated media contents for secrets.
- [ ] Review logs after failed auth and successful auth.
- [ ] Confirm least-privilege app registration can import devices.

### Phase 8: Documentation And Release Guardrails
- [ ] Add user documentation for hardware hash upload from WinPE.
- [ ] Mark WinPE hash capture as best-effort and not the Microsoft-standard method.
- [ ] Document x64-only V1 scope.
- [ ] Document Ethernet recommendation.
- [ ] Document unsupported or risky scenarios:
  - self-deploying mode
  - pre-provisioning
  - Wi-Fi-only devices
  - ARM64
  - missing TPM visibility
  - unsupported `PCPKsp.dll` redistribution
- [ ] Update screenshots after UI implementation.

Manual checks:
- [ ] Follow the docs on a clean test tenant.
- [ ] Follow the docs on a clean x64 test device.
- [ ] Confirm fallback to OOBE/full OS instructions are clear.

## Open Questions
- Should V1 upload directly from WinPE to Graph, or should it support capture-only with deferred upload first?
- Which authentication mode is acceptable for generated media?
- Can `PCPKsp.dll` be used or referenced without redistribution risk?
- Should ARM64 be explicitly blocked in hash upload mode for V1?
- Should duplicate device cleanup ever be added, or should Foundry only surface the duplicate and stop?

## Source References
- Microsoft Learn: [Manually register devices with Windows Autopilot](https://learn.microsoft.com/en-us/autopilot/add-devices)
- Microsoft Learn: [Microsoft Graph importedWindowsAutopilotDeviceIdentity import](https://learn.microsoft.com/en-us/graph/api/intune-enrollment-importedwindowsautopilotdeviceidentity-import?view=graph-rest-1.0)
- Microsoft Learn: [Using the OA 3.0 tool on the factory floor](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/oa3-using-on-factory-floor?view=windows-11)
- Microsoft Learn: [WinPE optional components reference](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-add-packages--optional-components-reference?view=windows-11)
- Local artifact: `C:\Users\mchav\Downloads\foundry-autopilot-hash-winpe-en.md`
- Local artifact: `C:\Users\mchav\Downloads\HashUpload_WinPE.ps1`


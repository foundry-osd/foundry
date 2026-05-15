# Autopilot Hardware Hash Upload Implementation Plan

## Purpose
This document defines the feasibility, integration approach, and phased implementation plan for adding Windows Autopilot hardware hash capture and upload from WinPE to Foundry.

This feature is intended to complement the existing offline Autopilot JSON profile staging flow. It must not replace the current behavior.

## Branch Strategy
- Foundation branch: `feature/autopilot-hash-upload-foundation`
- Foundation worktree: `C:\Users\mchav\.config\superpowers\worktrees\foundry\autopilot-hash-upload-foundation`
- Phase branches should branch from the foundation branch:
  - `feature/autopilot-hash-upload-config`
  - `feature/autopilot-hash-upload-ui`
  - `feature/autopilot-hash-upload-media`
  - `feature/autopilot-hash-upload-runtime`
  - `feature/autopilot-hash-upload-capture`
  - `feature/autopilot-hash-upload-graph`
  - `feature/autopilot-hash-upload-security`
  - `feature/autopilot-hash-upload-docs`

The foundation branch should remain documentation-first. Implementation branches should be small, reviewable, and merged back into the foundation branch in phase order.

## Pull Request Roadmap
All PR titles must stay in English and use Conventional Commits. Each phase branch should be merged back into the foundation branch before the next phase branch starts.

| Order | Branch | Pull request title | Scope |
| --- | --- | --- | --- |
| 0 | `feature/autopilot-hash-upload-foundation` | `docs(autopilot): plan hardware hash upload from WinPE` | Feasibility, phased integration plan, risk register, test matrix. |
| 1 | `feature/autopilot-hash-upload-config` | `feat(autopilot): add provisioning mode configuration` | Expert and Deploy configuration models, backward compatibility, readiness rules. |
| 2 | `feature/autopilot-hash-upload-ui` | `feat(autopilot): add provisioning method selection` | Autopilot page expanders, mutually exclusive method selection, localized strings. |
| 3 | `feature/autopilot-hash-upload-media` | `feat(winpe): stage autopilot hash capture assets` | WinPE optional component requirements, x64/ARM64 OA3Tool discovery, media payload layout. |
| 4 | `feature/autopilot-hash-upload-runtime` | `feat(deploy): branch autopilot runtime by provisioning mode` | Deploy startup snapshot, launch validation, runtime state, late deployment step, dry-run manifests. |
| 5 | `feature/autopilot-hash-upload-capture` | `feat(deploy): capture autopilot hardware hash in WinPE` | C# OA3Tool execution service, `PCPKsp.dll` copy, `OA3.xml` parsing, CSV/diagnostic artifacts. |
| 6 | `feature/autopilot-hash-upload-graph` | `feat(autopilot): import hardware hashes with Graph` | C# Graph client, import polling, retry policy, operator-facing errors. |
| 7 | `feature/autopilot-hash-upload-security` | `feat(autopilot): add secure tenant upload onboarding` | Tenant sign-in, app registration creation, certificate lifecycle, secret handling, permission validation. |
| 8 | `feature/autopilot-hash-upload-docs` | `docs(autopilot): document WinPE hardware hash upload` | User docs, permissions matrix, troubleshooting, screenshots, release notes. |

Expected PR description structure:
- Summary: one short paragraph.
- Reason: why this phase is needed and what risk it removes.
- Main changes: concise bullet list.
- Testing notes: exact automated commands and manual checks.

## Non-Goals
- Do not remove or redesign the existing offline JSON profile workflow.
- Do not involve Foundry Connect.
- Do not implement hardware hash capture or upload with PowerShell scripts, PowerShell Gallery modules, or community wrapper modules.
- Do not delete existing Intune, Autopilot, or Entra records automatically.
- Do not add group membership automation.
- Do not claim full Microsoft support for WinPE-based hash capture.
- Do not limit the implementation to x64. x64 and ARM64 are both in scope.
- Do not redistribute `PCPKsp.dll` in generated media. Copy it from the applied Windows image during deployment.

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
- `StageAutopilotConfigurationStep` has a profile-staging name but is the correct execution boundary for Autopilot mode branching.
- Media readiness only checks whether a selected profile exists when Autopilot is enabled.
- Deploy summary and telemetry only track `autopilot_enabled`, not the provisioning method.

## Target UX
On the Autopilot page:

- The user enables or disables Autopilot globally.
- When enabled, two settings expanders are shown:
  - Offline JSON profile provisioning.
  - Hardware hash capture and upload.
- Only one provisioning method can be active at a time.
- Existing JSON import, tenant profile download, default profile selection, and profile table remain inside the JSON provisioning expander.
- Hardware hash upload settings stay in a separate expander with its own validation and readiness text.

Suggested expander content:

JSON profile provisioning:
- Method toggle or radio selection: "Use offline Autopilot profile JSON".
- Existing import JSON action.
- Existing download from tenant action.
- Existing remove selected profiles action.
- Default profile selector.
- Existing profiles table.

Hardware hash upload:
- Method toggle or radio selection: "Capture and upload hardware hash".
- Default state: tenant connection prompt.
- Connected state:
  - Tenant identity summary.
  - Foundry-managed app registration status.
  - Single managed certificate status.
  - Certificate expiration state.
  - Certificate private key input for media generation.
  - Autopilot group tag default selection.
- Optional assigned user UPN field.
- Upload timing selector:
  - `CaptureAndUpload`
  - `CaptureOnly` for diagnostics only, if retained.
- Optional "wait for import completion" setting.
- Optional "wait for assignment" setting should be deferred unless proven reliable.
- Readiness and warning text for x64, ARM64, network connectivity, WinPE-SecureStartup, and unsupported scenarios.

UX rules:
- The global Autopilot toggle controls whether either method is active.
- Selecting one method immediately deselects the other.
- JSON profile data can remain stored while hash mode is selected, but it must not be required.
- Hash upload settings can remain stored while JSON mode is selected, but they must not be required.
- The Start page readiness summary should show the active method, not just "Autopilot enabled".

Foundry OSD tenant onboarding UX:
- The hardware hash expander first asks the user to connect to the tenant.
- After sign-in, Foundry OSD searches for the managed app registration.
- The planned app registration display name is `Foundry OSD Autopilot Registration`.
- If the app registration does not exist, Foundry OSD creates it with the required Microsoft Graph application permissions.
- If the app registration exists, Foundry OSD reuses it.
- Foundry OSD manages exactly one certificate on that app registration.
- If no certificate exists, show a create certificate action.
- If a certificate exists, show:
  - display name
  - thumbprint
  - start date
  - expiration date
  - expired/valid status
  - delete certificate action
  - replace certificate action
- Certificate creation requires selecting a validity duration from a fixed list.
- The default certificate validity is 12 months.
- Certificate validity options should be fixed, for example:
  - 3 months
  - 6 months
  - 12 months
  - 24 months
- When Foundry OSD creates a certificate, it shows the private key once in a content dialog.
- The content dialog must clearly state that the private key is shown only once and must be stored by the operator.
- Foundry OSD must not persist the raw private key after creation.
- On later application launches, the user must sign in again before Foundry OSD can inspect or manage the tenant app registration.
- Before media generation, the Autopilot page requires the private key for the currently provisioned certificate.
- The private key input should be visually close to the certificate status so the operator understands which certificate it belongs to.
- If the certificate is expired, Foundry OSD must show the expired status and require regenerating the certificate before the boot image can be built for hardware hash upload.
- When connected to the tenant, Foundry OSD should list existing Autopilot group tags discovered from Intune and let the user choose the default group tag passed to Foundry Deploy.

Foundry Deploy UX:
- Foundry Deploy should render only the selected Autopilot provisioning mode from Foundry OSD.
- JSON mode shows only the JSON/profile controls.
- Hardware hash mode shows only hardware hash upload controls.
- Hardware hash mode should attempt certificate-based Graph authentication automatically during startup/loading, before the Computer Target page becomes actionable.
- The Computer Target page should expose two mutually exclusive group tag choices:
  - select the default/existing group tag supplied by Foundry OSD
  - enter a custom group tag when the desired value does not exist
- If the certificate is expired in Foundry Deploy:
  - do not block the OS deployment
  - hide the group tag selection and custom group tag textbox
  - show a clear message that Graph connection cannot be established because the certificate is expired
  - tell the user to regenerate the certificate and recreate the boot image
  - skip Autopilot hash upload for that deployment run
- During the Autopilot provisioning step, after hash upload succeeds, Foundry Deploy must wait until the device appears in Intune Windows Autopilot devices before continuing.

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

Proposed expert model:

```csharp
public sealed record AutopilotSettings
{
    public bool IsEnabled { get; init; }
    public AutopilotProvisioningMode ProvisioningMode { get; init; } = AutopilotProvisioningMode.JsonProfile;
    public string? DefaultProfileId { get; init; }
    public IReadOnlyList<AutopilotProfileSettings> Profiles { get; init; } = [];
    public AutopilotHardwareHashUploadSettings HardwareHashUpload { get; init; } = new();
}
```

Proposed hash settings:

```csharp
public sealed record AutopilotHardwareHashUploadSettings
{
    public const string ManagedAppRegistrationDisplayName = "Foundry OSD Autopilot Registration";

    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? CertificateThumbprint { get; init; }
    public DateTimeOffset? CertificateNotAfter { get; init; }
    public SecretEnvelope? CertificatePrivateKeySecret { get; init; }
    public SecretEnvelope? CertificatePasswordSecret { get; init; }
    public string? DefaultGroupTag { get; init; }
    public IReadOnlyList<string> KnownGroupTags { get; init; } = [];
    public string? GroupTag { get; init; }
    public string? AssignedUserPrincipalName { get; init; }
    public AutopilotHashUploadMode UploadMode { get; init; } = AutopilotHashUploadMode.CaptureAndUpload;
    public bool WaitForImportCompletion { get; init; } = true;
}
```

Proposed enums:

```csharp
public enum AutopilotHashUploadMode
{
    CaptureOnly,
    CaptureAndUpload
}
```

Validation rules:
- `IsEnabled=false`: no Autopilot settings are required.
- `IsEnabled=true` and `JsonProfile`: selected profile must exist.
- `IsEnabled=true` and `HardwareHashUpload`: tenant ID, client ID, and certificate settings must be valid.
- `CaptureOnly`: Graph auth settings are optional.
- `CaptureAndUpload`: certificate-based Graph auth settings are required.
- Expired certificates make Foundry OSD hardware hash media generation not ready.
- Expired certificates in Foundry Deploy skip Autopilot upload without blocking the OS deployment.
- `AssignedUserPrincipalName`, when set, must look like a UPN but should not be treated as proof that the user exists.
- `GroupTag` must not contain commas and should stay ASCII-safe for CSV compatibility.

## Authentication Recommendation
The implementation must not use PowerShell for hardware hash capture or upload actions.

Recommended direction:
- Use direct Microsoft Graph REST calls through C# service abstractions.
- Invoke OA3Tool through the existing C# process execution patterns, not through PowerShell.
- Use least-privilege upload permissions:
  - `DeviceManagementServiceConfig.ReadWrite.All` for import.
  - `DeviceManagementServiceConfig.Read.All` only if read-only polling is separated.
- Defer destructive permissions:
  - `DeviceManagementManagedDevices.ReadWrite.All`
  - `Device.ReadWrite.All`
  - `GroupMember.ReadWrite.All`

Supported WinPE authentication:
- Microsoft Graph authentication inside WinPE must use certificate-based app-only auth only.
- The certificate private key material is injected into the generated boot image as an encrypted media secret.
- Device code flow, client secrets, and brokered upload are not supported WinPE authentication modes for this feature.

Private keys, client secrets, and tenant-wide destructive permissions must not be silently embedded into generated media.

Recommended auth decision:
- Use certificate-based app-only auth for unattended or near zero-touch WinPE upload.
- Avoid client secrets for generated media.

Open auth design choices:
- Whether Foundry OSD pre-validates tenant/app/certificate settings before media generation.
- Whether Foundry Deploy validates the app certificate thumbprint before requesting a token.

Secret handling rules:
- Do not write access tokens to disk.
- Do not log authorization headers, refresh tokens, client secrets, private keys, certificate raw data, or Graph request bodies containing hardware hashes unless explicitly redacted.
- Store embedded certificate private key material with the same envelope concept used by Foundry Connect personal Wi-Fi secrets.
- Require explicit user confirmation before embedding certificate private key material and document that the generated media becomes tenant-sensitive.
- Treat media encryption as plaintext avoidance and integrity protection, not as a strong security boundary, because the decrypt key must also be available in the boot image.
- Zero decrypted private key bytes and media secret key bytes as soon as they are no longer needed.
- Never write a decrypted PFX, PEM private key, access token, or refresh token to disk.

Existing Foundry Connect pattern:
- Foundry Core generates a random 32-byte media secret key with `RandomNumberGenerator`.
- Personal Wi-Fi passphrases are serialized as a `SecretEnvelope` with:
  - `kind`: `encrypted`
  - `algorithm`: `aes-gcm-v1`
  - `keyId`: `media`
  - base64url `nonce`, `tag`, and `ciphertext`
- The raw passphrase is omitted from `foundry.connect.config.json`.
- `WinPeMountedImageAssetProvisioningService` writes the 32-byte key to `X:\Foundry\Config\Secrets\media-secrets.key` only when encrypted secrets exist.
- Foundry Connect reads the key, decrypts the envelope, then zeroes the key bytes.

Autopilot certificate private key plan:
- Generalize the Connect-only secret envelope code into a shared Core/Deploy media secret protector.
- Reuse the same `aes-gcm-v1` envelope shape unless a binary envelope variant is required for PFX bytes.
- Store the encrypted certificate material in the Deploy Autopilot hash upload configuration, not as a plaintext PFX file.
- Reuse `X:\Foundry\Config\Secrets\media-secrets.key` for all encrypted media secrets in the same generated image, or rename it to a generic media secret key only if the path migration is handled cleanly.
- Add media provisioning validation so encrypted Autopilot secrets require a media secret key, and a media secret key cannot be written without at least one encrypted secret.
- Foundry Deploy should decrypt the certificate material in memory, create the Graph credential, and avoid writing decrypted key material back to disk.

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

After the import state reaches `complete`, Foundry Deploy should poll Windows Autopilot device identities until the uploaded serial number is visible in Intune. This is the operator-facing completion condition for the Autopilot provisioning step.

Minimum Graph permission matrix:

| Capability | Permission | Implementation status |
| --- | --- | --- |
| Import Autopilot device identity | `DeviceManagementServiceConfig.ReadWrite.All` | Required for `CaptureAndUpload`. |
| Poll imported device identity state | `DeviceManagementServiceConfig.Read.All` or `DeviceManagementServiceConfig.ReadWrite.All` | Required when waiting for completion. |
| Delete Autopilot device identity | `DeviceManagementServiceConfig.ReadWrite.All` | Deferred. Not automatic in the final hash upload workflow. |
| Delete Intune managed device | `DeviceManagementManagedDevices.ReadWrite.All` | Deferred. |
| Delete Entra device | `Device.ReadWrite.All` | Deferred. |
| Add device to group | `GroupMember.ReadWrite.All` | Deferred. |

Graph request rules:
- Prefer a direct HTTP client abstraction with typed request/response records.
- Keep the import client independent from OA3Tool execution.
- Include request correlation IDs in logs when available.
- Use bounded retries for transient `429`, `5xx`, and network failures.
- Do not retry deterministic validation failures.
- Treat expired certificate authentication as a non-blocking Autopilot skip in Foundry Deploy, not as a deployment failure.

## WinPE Hash Capture Strategy
Final capture path:

1. Stage architecture-specific `oa3tool.exe` from the local ADK for the selected WinPE architecture.
2. Run the hash upload workflow late in Foundry Deploy, after the Windows image has been applied.
3. Copy `<target Windows>\Windows\System32\PCPKsp.dll` to `X:\Windows\System32\PCPKsp.dll`.
4. Generate an OA3 config file and dummy input key file from C#.
5. Run OA3Tool through a C# process execution service:

```cmd
oa3tool.exe /Report /ConfigFile=.\OA3.cfg /NoKeyCheck /LogTrace=.\OA3.log
```

6. Read `OA3.xml`.
7. Extract:
   - hardware hash
   - serial number
8. Upload through the C# Microsoft Graph import service when `CaptureAndUpload` is selected.
9. Save diagnostics:
   - `OA3.xml`
   - `OA3.log`
   - generated CSV
   - Foundry upload result JSON

The implementation should add `WinPE-SecureStartup` to the default WinPE optional component set, even when Autopilot hardware hash upload is disabled. The package is small, and making it default avoids a mode-specific boot image difference while improving TPM visibility for Autopilot quality. Existing media already includes WMI, NetFX, Scripting, PowerShell, WinReCfg, DismCmdlets, StorageWMI, Dot3Svc, and EnhancedStorage. PowerShell may remain present as an existing WinPE optional component, but Foundry must not use it to perform hash capture or upload.

`PCPKsp.dll` must not be bundled in generated media. Copying it from the applied Windows image avoids redistributing the file with Foundry media and keeps the copied DLL aligned with the target OS architecture. If the file is missing or cannot be copied, the hash upload step should fail with a clear diagnostic and keep the rest of deployment behavior explicit.

Proposed WinPE paths:
- `X:\Foundry\Tools\OA3\oa3tool.exe`
- `X:\Foundry\Runtime\AutopilotHash\OA3.cfg`
- `X:\Foundry\Runtime\AutopilotHash\input.xml`
- `X:\Foundry\Runtime\AutopilotHash\OA3.xml`
- `X:\Foundry\Runtime\AutopilotHash\OA3.log`
- `X:\Foundry\Runtime\AutopilotHash\AutopilotHWID.csv`
- `X:\Foundry\Runtime\AutopilotHash\AutopilotUploadResult.json`
- `X:\Windows\System32\PCPKsp.dll`

Proposed retained log paths after deployment:
- `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash\OA3.xml`
- `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash\OA3.log`
- `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash\AutopilotHWID.csv`
- `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash\AutopilotUploadResult.json`

Failure taxonomy:
- `ToolMissing`: OA3Tool is not staged or cannot execute.
- `ToolFailed`: OA3Tool exits non-zero.
- `SupportLibraryMissing`: `PCPKsp.dll` is missing from the applied Windows image.
- `SupportLibraryCopyFailed`: `PCPKsp.dll` cannot be copied to `X:\Windows\System32`.
- `SupportLibraryLoadFailed`: OA3Tool cannot use the copied `PCPKsp.dll`.
- `ReportMissing`: `OA3.xml` was not created.
- `ReportInvalid`: `OA3.xml` cannot be parsed.
- `HashMissing`: `HardwareHash` is empty.
- `SerialMissing`: BIOS serial number cannot be read.
- `NetworkUnavailable`: Graph upload is requested but no network path is available.
- `CertificateExpired`: certificate-based Graph authentication cannot run because the media certificate is expired. Foundry Deploy skips Autopilot upload and continues OS deployment.
- `AuthenticationFailed`: token acquisition failed.
- `ImportFailed`: Graph accepted the request path but import state reports `error`.
- `ImportTimedOut`: import polling exceeded the configured timeout.
- `AutopilotDeviceTimedOut`: import completed but the device did not appear in Windows Autopilot devices before timeout.

## Phased Implementation

### Phase 0: Foundation Branch And Research
PR title: `docs(autopilot): plan hardware hash upload from WinPE`

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
PR title: `feat(autopilot): add provisioning mode configuration`

- [ ] Add `AutopilotProvisioningMode`.
- [ ] Extend `AutopilotSettings` with mode and hardware hash upload settings.
- [ ] Extend `DeployAutopilotSettings` with reduced runtime mode and upload settings.
- [ ] Add encrypted certificate private key settings for the required certificate-based upload path.
- [ ] Add tenant app registration identity, certificate expiration, known group tags, and default group tag settings.
- [ ] Update schema version handling if needed.
- [ ] Keep old configurations backward compatible as JSON profile mode.
- [ ] Update sanitization in `ExpertDeployConfigurationStateService`.
- [ ] Update `DeployConfigurationGenerator`.

Automated tests:
- [ ] Existing JSON profile config serializes and generates the same deploy output.
- [ ] Enabled JSON mode requires a selected profile.
- [ ] Enabled hash upload mode does not require a selected profile.
- [ ] Capture-and-upload mode requires tenant ID, client ID, and encrypted certificate private key material.
- [ ] Invalid certificate settings make Autopilot configuration not ready.
- [ ] Expired certificate settings make OSD media generation not ready for hardware hash upload.
- [ ] Certificate private key material is not serialized in plaintext.

Manual checks:
- [ ] Start Foundry with existing user config and confirm JSON profile mode is selected.
- [ ] Disable Autopilot and confirm no profile or hash settings are required.

### Phase 2: Autopilot Page UX
PR title: `feat(autopilot): add provisioning method selection`

- [ ] Replace single Autopilot action section with two settings expanders.
- [ ] Keep global Autopilot toggle.
- [ ] Move existing import/download/remove/default profile/table UI into JSON profile expander.
- [ ] Add hardware hash upload expander.
- [ ] Add tenant connection state, connect action, and connected tenant summary.
- [ ] Add managed app registration creation/reuse status for `Foundry OSD Autopilot Registration`.
- [ ] Add single-certificate lifecycle controls: create, delete, replace, expired state.
- [ ] Add certificate validity selection with a default of 12 months.
- [ ] Add one-time private key content dialog after certificate creation.
- [ ] Add private key input near the active certificate status for boot image generation.
- [ ] Add tenant-discovered Autopilot group tag list and default group tag selection.
- [ ] Enforce mutual exclusivity between JSON profile and hash upload modes.
- [ ] Add localized strings in English and French resources.
- [ ] Update readiness messages to include selected mode.

Automated tests:
- [ ] View model mode changes save state.
- [ ] Selecting JSON mode disables hash upload readiness requirements.
- [ ] Selecting hash upload mode disables JSON profile selection requirements.
- [ ] Hardware hash media generation is not ready when the connected app certificate is expired.
- [ ] Hardware hash media generation requires a private key for the active certificate.
- [ ] Creating a certificate exposes the private key once and never persists the raw private key.
- [ ] Busy state still blocks JSON profile import/download/remove commands.

Manual checks:
- [ ] Autopilot disabled: both expanders are unavailable or collapsed according to final UX decision.
- [ ] Autopilot enabled: both expanders are visible.
- [ ] Activating one method deactivates the other.
- [ ] JSON profile import and tenant download still work.
- [ ] Connect to a tenant with no app registration and confirm Foundry OSD creates `Foundry OSD Autopilot Registration`.
- [ ] Connect to a tenant with an existing managed app registration and confirm Foundry OSD reuses it.
- [ ] Create a certificate, verify the private key is shown once, close the dialog, and confirm it cannot be shown again.
- [ ] Expire or simulate an expired certificate and confirm the OSD page clearly requires regenerating the certificate before boot image creation.

### Phase 3: Media Build And WinPE Assets
PR title: `feat(winpe): stage autopilot hash capture assets`

- [ ] Add `WinPE-SecureStartup` to the default required optional components for all generated WinPE media.
- [ ] Locate and stage architecture-specific `oa3tool.exe` from the ADK for x64 and ARM64.
- [ ] Add hash capture templates under a Foundry-owned WinPE path.
- [ ] Add hash upload runtime configuration under `X:\Foundry\Config`.
- [ ] Write encrypted Autopilot certificate private key envelopes and the media secret key through the shared media secret provisioning path.
- [ ] Keep current profile JSON staging unchanged in JSON profile mode.
- [ ] Do not stage JSON profile folders in hash upload mode unless the user also keeps profiles for another purpose.
- [ ] Do not stage `PCPKsp.dll` during media build.

Automated tests:
- [ ] Media asset provisioning writes JSON profile assets only in JSON mode.
- [ ] Media asset provisioning writes hash upload assets only in hash mode.
- [ ] Missing `oa3tool.exe` produces a clear validation error.
- [ ] ADK asset resolution chooses the expected path for x64 and ARM64 media.
- [ ] Encrypted Autopilot secrets require a media secret key.
- [ ] A media secret key is rejected when no encrypted media secrets exist.
- [ ] `WinPE-SecureStartup` missing or not applicable is surfaced clearly during media preparation.

Manual checks:
- [ ] Build x64 ISO in JSON profile mode and confirm existing profile files are present.
- [ ] Build x64 ISO in hash upload mode and confirm OA3/hash assets are present.
- [ ] Build ARM64 ISO in JSON profile mode and confirm existing profile files are present.
- [ ] Build ARM64 ISO in hash upload mode and confirm OA3/hash assets are present.
- [ ] Confirm `WinPE-SecureStartup` is present in the mounted image package list.
- [ ] Confirm no plaintext private key or client secret is written to media.

### Phase 4: Foundry Deploy Runtime Branching
PR title: `feat(deploy): branch autopilot runtime by provisioning mode`

- [ ] Load Autopilot provisioning mode from deploy config.
- [ ] Expose mode in startup snapshot, preparation view model, launch request, deployment context, and runtime state.
- [ ] Expose hardware hash group tag selection mode in the Computer Target page.
- [ ] Update `DeploymentLaunchPreparationService` validation:
  - JSON mode requires selected profile.
  - Hash upload mode requires valid upload settings.
- [ ] Keep `StageAutopilotConfigurationStep` profile-only so JSON mode copies `AutopilotConfigurationFile.json`.
- [ ] Add a late hash upload deployment step after OS apply and before deployment finalization.
- [ ] Hash upload mode skips JSON staging and runs the hash capture/upload workflow from the late deployment step.
- [ ] Update deployment summary, logs, and telemetry with mode.
- [ ] Skip Autopilot upload without blocking OS deployment when the embedded certificate is expired.

Automated tests:
- [ ] JSON mode still stages the profile to `Windows\Provisioning\Autopilot`.
- [ ] Hash upload mode skips JSON staging.
- [ ] Dry run creates a hash-mode manifest without touching Graph.
- [ ] Hash upload step is ordered after the applied Windows root is available.
- [ ] Launch preparation rejects incomplete hash upload settings.
- [ ] Expired certificate state hides hardware hash group tag controls and leaves deployment start available.

Manual checks:
- [ ] Deploy dry-run in JSON mode.
- [ ] Deploy dry-run in hash upload mode.
- [ ] Confirm summary page displays the selected Autopilot method.
- [ ] In hash mode, confirm Computer Target shows only hardware hash controls.
- [ ] In JSON mode, confirm Computer Target shows only JSON profile controls.
- [ ] In hash mode with expired certificate, confirm Deploy shows the regeneration/recreate media message and still allows OS deployment.
- [ ] Confirm logs contain mode, hash capture diagnostics path, and upload state.

### Phase 5: Hash Capture Service
PR title: `feat(deploy): capture autopilot hardware hash in WinPE`

- [ ] Add a C# service that runs OA3Tool with controlled working directory paths.
- [ ] Add a C# service that copies `PCPKsp.dll` from `<target Windows>\Windows\System32` to `X:\Windows\System32`.
- [ ] Validate source and destination architecture assumptions for x64 and ARM64.
- [ ] Generate `OA3.cfg` and dummy input XML internally.
- [ ] Validate `OA3.xml` exists.
- [ ] Extract serial number and hardware hash.
- [ ] Write a local CSV artifact for troubleshooting.
- [ ] Preserve OA3 logs in Foundry deployment logs.
- [ ] Return structured failure codes for missing tool, `PCPKsp.dll` copy/load failure, empty hash, invalid XML, missing serial, and OA3 exit failure.

Automated tests:
- [ ] Resolves the applied Windows `System32` source path.
- [ ] Copies `PCPKsp.dll` to `X:\Windows\System32` before OA3Tool execution.
- [ ] Parses valid `OA3.xml`.
- [ ] Rejects missing `HardwareHash`.
- [ ] Rejects invalid XML.
- [ ] Generates CSV without quotes, extra columns, or Unicode encoding.
- [ ] Sanitizes commas from group tag and serial number.

Manual checks:
- [ ] Run on one x64 physical test device with Ethernet.
- [ ] Run on one x64 physical test device with Wi-Fi.
- [ ] Run on one ARM64 physical test device with Ethernet.
- [ ] Run on one ARM64 physical test device with Wi-Fi.
- [ ] Confirm generated hash imports manually in Intune.
- [ ] Confirm troubleshooting files are retained in logs.

### Phase 6: Graph Upload Service
PR title: `feat(autopilot): import hardware hashes with Graph`

- [ ] Add a minimal Graph Autopilot import client.
- [ ] Add certificate-based credential creation from decrypted in-memory certificate material.
- [ ] Reject any non-certificate authentication mode in WinPE.
- [ ] Implement import request.
- [ ] Implement polling for import completion.
- [ ] Implement polling until the uploaded serial number appears in Windows Autopilot devices.
- [ ] Map Graph errors to operator-readable messages.
- [ ] Add retry/backoff for transient HTTP failures.
- [ ] Keep destructive cleanup out of the final hash upload workflow.

Automated tests:
- [ ] Serializes import payload correctly.
- [ ] Sends hardware identifier in the expected Graph format.
- [ ] Decrypts certificate material in memory and does not write a decrypted PFX/private key to disk.
- [ ] Fails clearly when tenant ID, client ID, certificate thumbprint, or encrypted certificate material is missing.
- [ ] Treats expired certificate auth as skipped Autopilot, not failed deployment.
- [ ] Handles `complete`.
- [ ] Handles imported identity completion followed by Windows Autopilot device visibility.
- [ ] Handles `error` with device error code/name.
- [ ] Times out with a clear message.
- [ ] Retries transient failures only.

Manual checks:
- [ ] Import one test device into a test tenant.
- [ ] Confirm Group Tag appears in Intune.
- [ ] Confirm deployment waits until the device appears in Windows Autopilot devices.
- [ ] Confirm assignment sync behavior is documented, even if not waited on by the final implementation.
- [ ] Confirm duplicate device behavior is clear to the operator.

### Phase 7: Security And Tenant Onboarding
PR title: `feat(autopilot): add secure tenant upload onboarding`

- [ ] Add a permission matrix to user documentation.
- [ ] Add tenant/app registration guidance.
- [ ] Implement and document managed app registration discovery/creation with display name `Foundry OSD Autopilot Registration`.
- [ ] Implement and document single-certificate lifecycle management.
- [ ] Document certificate app-only auth as the only supported WinPE Graph authentication path.
- [ ] Document that generated media containing encrypted certificate private key material is tenant-sensitive.
- [ ] Generalize the existing Foundry Connect AES-GCM media secret envelope for Autopilot secrets.
- [ ] Document device code flow, client secrets, and brokered upload as unsupported WinPE authentication modes.
- [ ] Explicitly document unsupported secret embedding patterns.
- [ ] Add audit-safe logging rules.

Automated tests:
- [ ] Secret settings are never serialized into plain deploy config.
- [ ] Tampered encrypted certificate envelopes fail without leaking ciphertext, private key material, or certificate password data.
- [ ] Logs redact tokens, secrets, private key paths, and certificate material.

Manual checks:
- [ ] Review generated media contents and confirm certificate private key material is envelope-encrypted, not plaintext.
- [ ] Review logs after failed auth and successful auth.
- [ ] Confirm least-privilege app registration can import devices.

### Phase 8: Documentation And Release Guardrails
PR title: `docs(autopilot): document WinPE hardware hash upload`

- [ ] Add user documentation for hardware hash upload from WinPE.
- [ ] Mark WinPE hash capture as best-effort and not the Microsoft-standard method.
- [ ] Document x64 and ARM64 scope.
- [ ] Document that Foundry copies `PCPKsp.dll` from the applied OS to `X:\Windows\System32` late in deployment.
- [ ] Document network requirements for Ethernet and Wi-Fi.
- [ ] Document unsupported or risky scenarios:
  - self-deploying mode
  - pre-provisioning
  - missing TPM visibility
- [ ] Update screenshots after UI implementation.

Manual checks:
- [ ] Follow the docs on a clean test tenant.
- [ ] Follow the docs on a clean x64 test device.
- [ ] Follow the docs on a clean ARM64 test device.
- [ ] Confirm fallback to OOBE/full OS instructions are clear.

## Cross-Cutting Test Matrix
Automated commands for every implementation PR:

```powershell
dotnet test .\src\Foundry.slnx --configuration Debug /p:Platform=x64
```

CI must pass for:
- x64
- ARM64

ARM64 CI is blocking because hash upload support is in scope for both architectures.

Recommended focused test areas:
- `Foundry.Core.Tests`
  - configuration serialization
  - deploy configuration generation
  - media preflight readiness
  - WinPE asset provisioning
  - OA3 XML and CSV helpers if implemented in Core
- `Foundry.Deploy.Tests`
  - startup snapshot
  - preparation view model
  - launch validation
  - deployment step branching
  - hash capture parser/client abstractions if implemented in Deploy
- `Foundry.Telemetry.Tests`
  - event property policy if new Autopilot mode telemetry properties are added

Manual physical validation matrix:

| Scenario | Required before release |
| --- | --- |
| x64 physical device with Ethernet, user-driven Autopilot | Yes |
| x64 physical device with Wi-Fi, user-driven Autopilot | Yes |
| x64 physical device with TPM 2.0 visible in WinPE | Yes |
| x64 device with existing Autopilot registration | Yes, expected duplicate/error behavior must be clear. |
| ARM64 physical device with Ethernet, user-driven Autopilot | Yes |
| ARM64 physical device with Wi-Fi, user-driven Autopilot | Yes |
| ARM64 physical device with TPM 2.0 visible in WinPE | Yes |
| ARM64 device with existing Autopilot registration | Yes, expected duplicate/error behavior must be clear. |
| JSON profile mode regression on x64 and ARM64 media | Yes |
| Capture-only diagnostic mode | Yes if included. |
| Capture-and-upload mode | Yes |
| Self-deploying/pre-provisioning | No, document as not recommended until separately validated. |

## Risk Register
| Risk | Impact | Mitigation |
| --- | --- | --- |
| OA3Tool produces empty or incomplete hash in WinPE | Import fails or device gets unreliable Autopilot behavior | Add `WinPE-SecureStartup`, retain OA3 diagnostics, document fallback to OOBE/full OS. |
| TPM not visible from WinPE | Self-deploying/pre-provisioning unreliable | Do not recommend those scenarios until separately validated. |
| Credentials embedded into media | Tenant compromise if generated media is lost | Encrypt certificate material with the media secret envelope, require explicit confirmation, document generated media as tenant-sensitive, and never write decrypted key material to disk. |
| Media secret key and encrypted secret are both present in the boot image | Encryption can be bypassed by anyone with full media access | Treat envelope encryption as plaintext avoidance/integrity protection, not a hard security boundary. |
| Certificate expires before deployment | Autopilot upload cannot authenticate | Show expired state in Foundry OSD, block hardware hash media generation until certificate regeneration, and let Foundry Deploy continue OS deployment while skipping Autopilot upload. |
| Operator loses one-time private key | Existing app certificate cannot be used for new media | Require creating/replacing the single managed certificate and rebuilding boot media. |
| Broad Graph permissions copied from community script | Excessive tenant blast radius | Minimum permission matrix and no destructive final implementation flows. |
| Duplicate devices already exist | Import fails or operator confusion | Surface duplicate/import error clearly; defer cleanup automation. |
| Architecture-specific OA3Tool/support file mismatch | Runtime failure | Resolve ADK assets per selected WinPE architecture and validate both x64 and ARM64 media. |
| `PCPKsp.dll` missing from applied OS or copy fails | Hash capture fails late in deployment | Copy from `<target Windows>\Windows\System32` after OS apply, fail clearly, and retain diagnostics. |
| UI conflates JSON and hash mode | Invalid media or deployment launch | Explicit `ProvisioningMode` and readiness rules. |

## Implementation Boundaries
Foundry app owns:
- User-facing Autopilot mode selection.
- Tenant sign-in for Autopilot hardware hash onboarding.
- Managed app registration discovery and creation.
- Single-certificate lifecycle management.
- One-time private key presentation.
- Autopilot group tag discovery and default selection.
- Expert configuration persistence.
- Media readiness and media generation.
- Tenant setting pre-validation where possible.

Foundry.Core owns:
- Shared configuration records and enums.
- Deploy configuration generation.
- WinPE media asset provisioning.
- Low-level helpers that are independent of WPF/WinUI.

Foundry.Deploy owns:
- Runtime configuration consumption.
- Deployment wizard behavior.
- Hardware hash Computer Target mode display.
- Certificate expiration detection and non-blocking Autopilot skip.
- Runtime group tag selection or custom group tag input.
- Late deployment workflow step after OS apply.
- `PCPKsp.dll` copy from the applied Windows image to `X:\Windows\System32`.
- OA3Tool execution through C# process orchestration.
- Graph import through C# service abstractions.
- Polling until the imported device appears in Windows Autopilot devices.
- Deployment logs and summary artifacts.

Foundry.Connect owns:
- Nothing for this feature.

## Documentation Deliverables
- Foundry app documentation:
  - Autopilot provisioning modes.
  - Hardware hash upload setup.
  - Tenant app registration onboarding.
  - Certificate creation, one-time private key handling, expiration, and replacement.
  - Group tag default selection.
  - Tenant permissions.
  - Security warning for generated media.
  - Troubleshooting.
- Foundry OSD docs site:
  - New Autopilot hardware hash upload page.
  - Requirements update documenting `WinPE-SecureStartup` as a default WinPE optional component.
  - Product boundaries update explaining the workaround status.
  - Manual test checklist.
- Release notes:
  - Mark as x64 and ARM64 with Ethernet and Wi-Fi upload guidance.
  - Mention unsupported or risky self-deploying/pre-provisioning status.

## Open Questions
- Should the final implementation keep a capture-only diagnostic mode in addition to capture-and-upload?
- Should `PCPKsp.dll` copy failure stop the full deployment, or only stop the Autopilot upload step?
- Should duplicate device cleanup ever be added, or should Foundry only surface the duplicate and stop?

## Source References
- Microsoft Learn: [Manually register devices with Windows Autopilot](https://learn.microsoft.com/en-us/autopilot/add-devices)
- Microsoft Learn: [Microsoft Graph importedWindowsAutopilotDeviceIdentity import](https://learn.microsoft.com/en-us/graph/api/intune-enrollment-importedwindowsautopilotdeviceidentity-import?view=graph-rest-1.0)
- Microsoft Learn: [Using the OA 3.0 tool on the factory floor](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/oa3-using-on-factory-floor?view=windows-11)
- Microsoft Learn: [WinPE optional components reference](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-add-packages--optional-components-reference?view=windows-11)
- Local artifact: `C:\Users\mchav\Downloads\foundry-autopilot-hash-winpe-en.md`
- Local artifact: `C:\Users\mchav\Downloads\HashUpload_WinPE.ps1`

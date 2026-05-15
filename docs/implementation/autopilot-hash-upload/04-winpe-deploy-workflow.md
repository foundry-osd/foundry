# Autopilot Hardware Hash Upload - WinPE And Deploy Workflow

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

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
8. Upload through the C# Microsoft Graph import service.
9. Save diagnostics:
   - `OA3.xml`
   - `OA3.log`
   - generated CSV
   - Foundry upload result JSON

The implementation should add `WinPE-SecureStartup` to the default WinPE optional component set, even when Autopilot hardware hash upload is disabled. The package is small, and making it default avoids a mode-specific boot image difference while improving TPM visibility for Autopilot quality. Existing media already includes WMI, NetFX, Scripting, PowerShell, WinReCfg, DismCmdlets, StorageWMI, Dot3Svc, and EnhancedStorage. PowerShell may remain present as an existing WinPE optional component, but Foundry must not use it to perform hash capture or upload.

`PCPKsp.dll` must not be bundled in generated media. Copying it from the applied Windows image avoids redistributing the file with Foundry media and keeps the copied DLL aligned with the target OS architecture. If the file is missing, cannot be copied, or cannot be loaded by OA3Tool, the Autopilot hash upload workflow must fail as a blocking Autopilot prerequisite failure.

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

Artifact retention rules:
- Retain Autopilot troubleshooting artifacts under the existing Foundry retained-artifact root: `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash`.
- This aligns with `FinalizeDeploymentAndWriteLogsStep`, which already rebinds deployment logs and state under `<target Windows>\Windows\Temp\Foundry` near the end of deployment.
- The retained file set is allow-listed to `OA3.xml`, `OA3.log`, `AutopilotHWID.csv`, and a sanitized `AutopilotUploadResult.json`.
- `AutopilotUploadResult.json` may contain timestamps, serial number, import identifier, active certificate thumbprint, Graph status, and operator-facing error text.
- Retained artifacts, deployment state, deployment summary, and general log files must not contain access tokens, authorization headers, raw Graph request bodies, raw Graph response bodies, PFX bytes, PFX password, decrypted private key material, encrypted secret blobs, full certificate data, or media secret keys.
- If the retained `AutopilotHash` folder inherits permissions broader than SYSTEM and Administrators, Foundry Deploy should tighten the ACL before finalization.
- No automatic purge is planned for the first implementation. Phase 8 documentation should explain that these files are retained for troubleshooting and how an operator can remove them after diagnostics are no longer needed.

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
- `CertificateMismatch`: the embedded PFX leaf certificate thumbprint does not match the configured active thumbprint.
- `CertificateMissing`: the media does not contain the encrypted PFX or password required for upload.
- `PermissionMissing`: the managed app does not have the required Microsoft Graph application permissions.
- `ConsentMissing`: required Graph application permissions do not have admin consent.
- `ServicePrincipalUnavailable`: the managed service principal is missing, disabled, or unusable.
- `ConditionalAccessBlocked`: app-only token acquisition or Graph access is blocked by tenant policy.
- `IntuneUnavailable`: Intune or the Windows Autopilot Graph endpoint is unavailable for the tenant.
- `AuthenticationFailed`: token acquisition failed.
- `ImportFailed`: Graph accepted the request path but import state reports `error`.
- `ImportTimedOut`: import polling exceeded the configured timeout.
- `AutopilotDeviceTimedOut`: import completed but the device did not appear in Windows Autopilot devices before the 10-minute wait timeout. Foundry Deploy logs a warning and continues OS deployment.

Support library failures are blocking for the hardware hash upload workflow because `PCPKsp.dll` is a prerequisite for reliable OA3Tool hash capture in this design. They must be represented as Autopilot prerequisite failures, not as non-blocking tenant/auth skips.
Import, duplicate-device, and visibility timeout failures are non-blocking Autopilot failures. Foundry Deploy should surface them clearly, retain sanitized diagnostics, and continue to the next OS deployment step.


## Implementation Boundaries
Foundry app owns:
- User-facing Autopilot mode selection.
- Tenant sign-in for Autopilot hardware hash onboarding.
- Managed app registration discovery and creation.
- Active certificate lifecycle management by Graph `keyId` and thumbprint.
- One-time PFX/private key material presentation.
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
- Late mode-aware Autopilot deployment step after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- `PCPKsp.dll` copy from the applied Windows image to `X:\Windows\System32`.
- OA3Tool execution through C# process orchestration.
- Graph import through C# service abstractions.
- Polling until the imported device appears in Windows Autopilot devices, with a visible 10-minute countdown and non-blocking timeout.
- Deployment logs and summary artifacts.

Foundry.Connect owns:
- Nothing for this feature.



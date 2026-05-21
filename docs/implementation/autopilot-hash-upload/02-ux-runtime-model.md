# Autopilot Hardware Hash Upload - UX And Runtime Model

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

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
  - Active certificate status.
  - Certificate expiration state.
  - Password-protected PFX input for media generation.
  - Autopilot group tag default selection.
- Optional assigned user UPN field.
- No user-facing wait option. Foundry Deploy always waits for import/device visibility with the default countdown and timeout behavior.
- No profile assignment wait in the final implementation.
- Readiness and warning text for x64, ARM64, network connectivity, WinPE-SecureStartup, and unsupported scenarios.

UX rules:
- The global Autopilot toggle controls whether either method is active.
- Selecting one method immediately deselects the other.
- JSON profile data can remain stored while hash mode is selected, but it must not be required.
- Hash upload settings can remain stored while JSON mode is selected, but they must not be required.
- The Start page readiness summary should show the active method, not just "Autopilot enabled".

Foundry OSD tenant onboarding UX:
- The hardware hash expander first asks the user to connect to the tenant.
- `Connect tenant` uses the official Foundry multi-tenant public client app as the interactive sign-in bootstrap. This app has client ID `83eb3a92-030d-49b7-881b-32a1eb3e110a` and is separate from the tenant-local app used by generated WinPE media.
- Persisted tenant metadata must not be displayed as fresh tenant state on application startup. Until the current app session successfully connects to Microsoft Graph, show only the tenant connection row with `Not connected` and the connect action.
- A successful tenant connection is retained for the current app session across page navigation. Foundry OSD should require a new tenant sign-in only after the app process restarts or after the operator clicks `Disconnect tenant`.
- Certificate creation and certificate removal must reuse the current app-session Microsoft Graph credential created by `Connect tenant`. They must not start their own interactive sign-in flow during the same app session.
- After successful current-session tenant connection, show tenant-dependent rows: tenant details, app registration status, onboarding status, certificates, default group tag, and available group tags.
- The tenant connection row shows only the connection state and action. Tenant ID and other tenant metadata appear in a separate tenant details row after connection.
- After a successful current-session connection, the connection action changes to a disconnect action that clears only the current UI session state. Persisted tenant configuration remains stored.
- After sign-in, Foundry OSD searches for the managed app registration.
- The planned app registration display name is `Foundry OSD Autopilot Registration`.
- This managed app registration is tenant-local and is the only application identity used by Foundry Deploy in WinPE for certificate-based Graph authentication.
- If the app registration does not exist, Foundry OSD creates it with the required Microsoft Graph application permissions.
- If the app registration exists and matches the persisted application object ID, Foundry OSD reuses it.
- If an app registration with the same display name exists but Foundry has no persisted application object ID, Foundry OSD must enter a repair/adoption state instead of silently taking ownership.
- Foundry OSD must persist tenant ID, application object ID, application/client ID, service principal object ID, and the certificate metadata resolved from the selected boot media PFX.
- The app registration may contain multiple certificate credentials. Tenant readiness is based on at least one valid Foundry-managed app certificate credential being present, not on a single persisted active certificate.
- Foundry must not assume exclusive ownership of the app registration and must not automatically delete, replace, or prune non-active certificate credentials.
- Extra certificate credentials on the app are tolerated and shown in a selectable certificate table, not as a blocking error.
- The certificate action buttons should be above the certificate table.
- The certificate table should show thumbprint, creation date, expiration date, and Graph certificate ID. It supports multi-row selection, and removal is enabled only when at least one certificate row is selected.
- Certificate removal must remove only the selected Graph `keyId` credentials and preserve every unselected app credential.
- The certificate row should not show a separate "valid until" message when the same expiration is already visible in the table.
- If Graph returns no certificate credentials, do not show a warning message in the certificate table area; the create certificate action is enough.
- Certificate validity text should use WinUI signal brushes: success for valid certificates, caution for certificates expiring within 30 days, and critical for expired certificates.
- Certificate creation requires selecting a validity duration from a fixed list.
- The default certificate validity is 6 months.
- Certificate validity options are fixed:
  - 1 month
  - 3 months
  - 6 months
  - 12 months
- Certificate creation produces a password-protected PFX only. PEM keys, unprotected private keys, and client secrets are not supported.
- Certificate creation requires the operator to choose a PFX output path.
- Certificate creation generates a strong PFX password.
- When Foundry OSD creates a certificate, it writes the PFX to the selected output path and shows the generated password once in a selectable, read-only field.
- The content dialog must clearly state that the PFX and PFX password must be stored by the operator outside Foundry.
- Foundry OSD must not persist the raw PFX, PFX password, decrypted private key, exported private key material, or a DPAPI-encrypted local PFX vault in ProgramData.
- Foundry OSD may keep the PFX bytes and password in memory for the current app session only, so the operator can create the boot image immediately after certificate creation without selecting the same PFX again.
- On later application launches, the user must sign in again before Foundry OSD can inspect or manage the tenant app registration.
- After Foundry OSD restarts, before media generation, the Autopilot page requires selecting the password-protected PFX again and entering its password. The PFX leaf certificate thumbprint must match one of the non-expired app registration certificate credentials.
- The PFX input is a dedicated "Boot media certificate" row directly after the tenant certificate table. The certificate table represents credentials present in Entra; the boot media certificate row represents the local private key material selected for the generated media.
- The boot media certificate row contains a read-only PFX path field, a PFX file picker, a password box, and validation status.
- When Foundry OSD creates a certificate, the current app session automatically uses the generated PFX path and password for the boot media certificate row.
- Foundry OSD must validate that the supplied PFX leaf certificate thumbprint matches a non-expired certificate credential currently provisioned on the managed app registration before media generation.
- Foundry OSD must keep the selected PFX path, PFX password, and validation result in memory only for the current app session and must not serialize them to ProgramData.
- If the certificate is expired, Foundry OSD must show the expired status and require regenerating the certificate before the boot image can be built for hardware hash upload.
- If the selected boot media PFX certificate is missing from Graph, expired, or no longer matches any provisioned certificate credential, Foundry OSD must show a repair state before allowing hash-upload media generation.
- Removing certificates must not show a success dialog. The refreshed certificate table and readiness state are the confirmation.
- App registration permission and consent state must be explicit:
  - app registration missing
  - app registration created
  - required Graph application permissions missing
  - admin consent missing
  - service principal disabled or missing
  - ready for media build
- The onboarding status row should show only `Ready` or `Not ready` with WinUI signal color: success for ready and critical for not ready. A successful tenant connection should not show a success content dialog; the inline readiness row is enough. Detailed failure reasons remain available through attention/failure dialogs and Start page readiness blockers.
- Foundry OSD must block hardware hash media generation until the managed app exists, required permissions are present, admin consent is granted, the service principal is usable, at least one non-expired app registration certificate is provisioned, and the supplied PFX matches a provisioned certificate.
- The Start page must show the precise Autopilot readiness blocker instead of a generic default-profile message. Expected hardware hash blockers include missing tenant/app metadata, missing provisioned certificate, expired selected certificate, missing PFX, missing PFX password, unvalidated PFX, thumbprint mismatch, and expired boot media PFX.
- When connected to the tenant, Foundry OSD should list existing Autopilot group tags discovered from `GET /v1.0/deviceManagement/windowsAutopilotDeviceIdentities`, extracting `groupTag` client-side from the unfiltered response and paging through `@odata.nextLink`. The UI label is "Available group tags".

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
- Any tenant, certificate, token, consent, permission, Conditional Access, Intune availability, or Graph connectivity failure in Foundry Deploy must skip only the Autopilot hash upload and must not block the OS deployment.
- Any Graph import failure, duplicate-device import failure, import polling timeout, or Windows Autopilot device visibility timeout must also skip/fail only the Autopilot workflow for that deployment run and continue OS deployment with a clear warning.
- During the Autopilot provisioning step, after hash upload succeeds, Foundry Deploy must wait until the device appears in Intune Windows Autopilot devices before treating the Autopilot step as complete.
- While waiting for the device to appear, Foundry Deploy should show an indeterminate sub-progress indicator and a countdown showing the time remaining before the wait times out.
- The default Windows Autopilot device visibility wait timeout is 10 minutes.
- If the wait reaches the 10-minute timeout, Foundry Deploy should automatically continue to the next OS deployment step, mark Autopilot visibility waiting as timed out/skipped, and retain a clear warning in the deployment summary and logs.
- Waiting for import completion and Windows Autopilot device visibility is mandatory internal behavior, not a user-facing option.

Deployment workflow placement:
- The Autopilot workflow should remain a single late deployment step after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- The current JSON-specific `StageAutopilotConfigurationStep` should be renamed conceptually to `ProvisionAutopilotStep`.
- JSON mode should keep the current behavior inside this step: copy `AutopilotConfigurationFile.json` into `<target Windows>\Windows\Provisioning\Autopilot`.
- Hardware hash mode should run inside the same step after the applied Windows root is available: copy `PCPKsp.dll`, run OA3Tool, upload the hash, wait for Windows Autopilot device visibility, and retain diagnostics.
- Keeping both modes under one Autopilot step avoids splitting Autopilot behavior across unrelated deployment phases and keeps final artifact relocation in `FinalizeDeploymentAndWriteLogs`.


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
    public string ApplicationObjectId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? ServicePrincipalObjectId { get; init; }
    public Guid? CertificateKeyId { get; init; }
    public string? CertificateThumbprint { get; init; }
    public DateTimeOffset? CertificateNotAfter { get; init; }
    public string? DefaultGroupTag { get; init; }
    public IReadOnlyList<string> KnownGroupTags { get; init; } = [];
    public string? GroupTag { get; init; }
    public string? AssignedUserPrincipalName { get; init; }
}
```

Validation rules:
- `IsEnabled=false`: no Autopilot settings are required.
- `IsEnabled=true` and `JsonProfile`: selected profile must exist.
- `IsEnabled=true` and `HardwareHashUpload`: tenant ID, application object ID, client ID, service principal state, active certificate key ID, active certificate thumbprint, and certificate expiration must be valid.
- Hardware hash upload always captures and uploads. There is no first-implementation capture-only mode.
- Expired certificates make Foundry OSD hardware hash media generation not ready.
- Expired certificates in Foundry Deploy skip Autopilot upload without blocking the OS deployment.
- `AssignedUserPrincipalName`, when set, must look like a UPN but should not be treated as proof that the user exists.
- `GroupTag` must not contain commas and should stay ASCII-safe for CSV compatibility.

Deploy media generation should add encrypted `CertificatePfxSecret` and `CertificatePfxPasswordSecret` only to the generated WinPE deploy configuration for the current media build. These secret envelopes are not persisted in the normal Foundry OSD settings stored under ProgramData, and Foundry OSD does not maintain a local encrypted PFX vault.

Phase 2 UX refinements:
- Settings cards include concise descriptions for row-level intent.
- JSON profile download and hardware hash tenant onboarding use the same tenant operation dialog runner for Microsoft Graph sign-in, progress, and cancellation.
- Canceling the tenant operation dialog returns control to the Autopilot page without leaving the connect/download action disabled.
- Hardware hash certificate management uses a shared current-session Graph credential, so create/remove certificate actions do not reopen the browser sign-in after a successful tenant connection.
- Tenant details are displayed as a table with tenant ID and client ID.
- The default group tag is optional and selected from a ComboBox populated with `None` plus tenant-discovered Autopilot device group tags.
- `None` is the default selection because hardware hash upload does not require a group tag.
- Available group tags are displayed as a one-column table for scanability.
- Certificate validity remains selectable, but the inline `Validity` label is hidden to reduce duplicate text in the certificate row.
- Current-session PFX path, password, and successful validation state are retained while navigating between pages, but are still cleared on app restart.
- The Start page must show the exact hardware hash media generation blocker when the PFX path, password, thumbprint, expiration, or active certificate is invalid.
- The onboarding status row is intentionally compact: `Ready` or `Not ready`, with signal color, while detailed remediation stays in dialogs and readiness blockers.

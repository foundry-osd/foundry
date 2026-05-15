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
- After sign-in, Foundry OSD searches for the managed app registration.
- The planned app registration display name is `Foundry OSD Autopilot Registration`.
- If the app registration does not exist, Foundry OSD creates it with the required Microsoft Graph application permissions.
- If the app registration exists and matches the persisted application object ID, Foundry OSD reuses it.
- If an app registration with the same display name exists but Foundry has no persisted application object ID, Foundry OSD must enter a repair/adoption state instead of silently taking ownership.
- Foundry OSD must persist tenant ID, application object ID, application/client ID, service principal object ID, active certificate key ID, active certificate thumbprint, and active certificate expiration.
- The app registration may contain multiple certificate credentials. Foundry tracks exactly one active Foundry certificate by `keyId` and leaf certificate thumbprint.
- Foundry must not assume exclusive ownership of the app registration and must not automatically delete, replace, or prune non-active certificate credentials.
- Extra certificate credentials on the app are tolerated and shown as a warning or informational state, not as a blocking error.
- If no active certificate exists, show a create certificate action.
- If an active certificate exists, show:
  - display name
  - thumbprint
  - Graph `keyId`
  - start date
  - expiration date
  - expired/valid status
  - retire active certificate action
  - replace active certificate action
- Certificate creation requires selecting a validity duration from a fixed list.
- The default certificate validity is 12 months.
- Certificate validity options should be fixed, for example:
  - 3 months
  - 6 months
  - 12 months
  - 24 months
- Certificate creation produces a password-protected PFX only. PEM keys, unprotected private keys, and client secrets are not supported.
- Certificate creation requires the operator to choose a PFX output path.
- Certificate creation should let the operator generate a strong PFX password or enter a custom PFX password.
- When Foundry OSD creates a certificate, it writes the PFX to the selected output path and shows the generated password once if Foundry generated it.
- The content dialog must clearly state that the PFX and PFX password must be stored by the operator outside Foundry.
- Foundry OSD must not persist the raw PFX, PFX password, decrypted private key, exported private key material, or a DPAPI-encrypted local PFX vault in ProgramData.
- Foundry OSD may keep the PFX bytes and password in memory for the current app session only, so the operator can create the boot image immediately after certificate creation without selecting the same PFX again.
- On later application launches, the user must sign in again before Foundry OSD can inspect or manage the tenant app registration.
- After Foundry OSD restarts, before media generation, the Autopilot page requires selecting the password-protected PFX again and entering its password for the currently active certificate.
- The PFX input should be visually close to the active certificate status so the operator understands which certificate it belongs to.
- Foundry OSD must validate that the supplied PFX leaf certificate thumbprint matches the active certificate thumbprint before media generation.
- If the certificate is expired, Foundry OSD must show the expired status and require regenerating the certificate before the boot image can be built for hardware hash upload.
- If the active certificate is missing from Graph, expired, or no longer matches the persisted `keyId` and thumbprint, Foundry OSD must show a repair state before allowing hash-upload media generation.
- Replacing or retiring the active certificate must warn that previously generated boot images using the old certificate may no longer authenticate once that credential is removed from the app registration.
- If multiple Foundry-looking certificates exist but no active certificate is persisted, Foundry OSD must require the operator to choose one active certificate by thumbprint and validate a matching password-protected PFX, or replace them with a new active certificate.
- App registration permission and consent state must be explicit:
  - app registration missing
  - app registration created
  - required Graph application permissions missing
  - admin consent missing
  - service principal disabled or missing
  - ready for media build
- Foundry OSD must block hardware hash media generation until the managed app exists, required permissions are present, admin consent is granted, the service principal is usable, the active certificate is unexpired, and the supplied PFX matches the active certificate.
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



# Autopilot Hardware Hash Upload - Security And Graph

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

## Authentication Recommendation
The implementation must not use PowerShell for hardware hash capture or upload actions.

Recommended direction:
- Use direct Microsoft Graph REST calls through C# service abstractions.
- Invoke OA3Tool through the existing C# process execution patterns, not through PowerShell.
- Split OSD interactive onboarding permissions from WinPE app-only upload permissions.
- Use least-privilege WinPE app-only upload permissions:
  - `DeviceManagementServiceConfig.ReadWrite.All` for import and polling.
- Defer destructive permissions:
  - `DeviceManagementManagedDevices.ReadWrite.All`
  - `Device.ReadWrite.All`
  - `GroupMember.ReadWrite.All`

Permission split:

| Surface | Authentication context | Required capability | Stored in boot media |
| --- | --- | --- | --- |
| Foundry OSD tenant onboarding | Interactive signed-in admin user | Create/reuse app registration, add Graph application permissions, grant or verify admin consent, add/retire app certificate credentials, read Autopilot group tags. | No |
| Foundry Deploy in WinPE | App-only certificate credential from generated media | Import the captured hardware hash and poll import/device visibility. | Yes, as encrypted PFX envelope plus media secret key |

The interactive OSD user may need broad Entra application management rights during setup, but those delegated/session permissions are not embedded into the boot image. The boot image receives only the managed app identity and certificate material needed for Autopilot import.

OSD setup permission guidance:
- `Application.ReadWrite.All` is needed when Foundry OSD creates or updates the managed app registration, including required resource access and certificate credentials.
- `AppRoleAssignment.ReadWrite.All` is needed if Foundry OSD grants the Microsoft Graph application role to the managed service principal instead of only detecting that admin consent is missing.
- If the signed-in operator does not have enough rights to grant consent, Foundry OSD should show a consent-required state and block hash-upload media generation until the tenant admin completes consent.
- These setup rights belong to the signed-in OSD operator session only. They are not stored, exported, or embedded in generated media.

Supported WinPE authentication:
- Microsoft Graph authentication inside WinPE must use certificate-based app-only auth only.
- The certificate private key material is injected into the generated boot image as an encrypted media secret.
- The injected certificate format is a password-protected PFX whose leaf certificate thumbprint matches the active certificate configured on the managed app registration.
- Device code flow, client secrets, and brokered upload are not supported WinPE authentication modes for this feature.

Private keys, client secrets, and tenant-wide destructive permissions must not be silently embedded into generated media.

Recommended auth decision:
- Use certificate-based app-only auth for unattended or near zero-touch WinPE upload.
- Avoid client secrets for generated media.

Secret handling rules:
- Do not write access tokens to disk.
- Do not log authorization headers, refresh tokens, client secrets, private keys, certificate raw data, or Graph request bodies containing hardware hashes unless explicitly redacted.
- Store embedded certificate private key material with the same envelope concept used by Foundry Connect personal Wi-Fi secrets.
- Accept only password-protected PFX input for media generation.
- Reject unprotected PFX files, PEM private keys, and PFX files whose leaf certificate thumbprint does not match the active certificate thumbprint.
- Require explicit user confirmation before embedding certificate private key material and document that the generated media becomes tenant-sensitive.
- Do not add a "remember this PFX" option or store a DPAPI-encrypted copy in ProgramData.
- Keep operator-provided PFX bytes and PFX password in memory only for the current app session, then clear them when media generation finishes or the app closes.
- Treat media encryption as plaintext avoidance and integrity protection, not as a strong security boundary, because the decrypt key must also be available in the boot image.
- Zero decrypted private key bytes and media secret key bytes as soon as they are no longer needed.
- Never write a decrypted PFX, PFX password, PEM private key, access token, or refresh token to disk except for the operator-selected PFX output created during certificate export.

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

After the import state reaches `complete`, Foundry Deploy should poll Windows Autopilot device identities until the uploaded serial number is visible in Intune. This is the operator-facing completion condition for the Autopilot provisioning step. The wait should use a 10-minute default timeout with a visible countdown. Timeout is non-blocking for OS deployment because Intune can rarely take longer than 10 minutes to surface the device.

Minimum Graph permission matrix:

| Capability | Permission | Implementation status |
| --- | --- | --- |
| Import Autopilot device identity | `DeviceManagementServiceConfig.ReadWrite.All` | Required. |
| Poll imported device identity state | `DeviceManagementServiceConfig.ReadWrite.All` | Required. |
| Poll Windows Autopilot device visibility | `DeviceManagementServiceConfig.ReadWrite.All` | Required. |
| Delete Autopilot device identity | `DeviceManagementServiceConfig.ReadWrite.All` | Deferred. Not automatic in the final hash upload workflow. |
| Delete Intune managed device | `DeviceManagementManagedDevices.ReadWrite.All` | Deferred. |
| Delete Entra device | `Device.ReadWrite.All` | Deferred. |
| Add device to group | `GroupMember.ReadWrite.All` | Deferred. |

The supplied community script deletes existing records before import to force a clean re-registration path when a serial number already exists in Intune, Windows Autopilot, or Entra ID. That can be useful in a controlled technician script because it removes stale or duplicate records before importing the new hash, but it requires destructive tenant-wide permissions and can remove records the operator did not intend to delete. Foundry's final implementation should not do this automatically. It should surface duplicate/import errors, keep diagnostics, and continue OS deployment.

Graph request rules:
- Prefer a direct HTTP client abstraction with typed request/response records.
- Keep the import client independent from OA3Tool execution.
- Include request correlation IDs in logs when available.
- Use bounded retries for transient `429`, `5xx`, and network failures.
- Do not retry deterministic validation failures.
- For application certificate management, merge the existing `keyCredentials` collection with the new certificate credential instead of replacing the collection with only Foundry's credential.
- Remove only the active Foundry-managed certificate credential identified by the persisted `keyId`, and never remove unknown or non-active certificate credentials automatically.
- Treat any certificate, tenant, token, permission, consent, Conditional Access, Intune availability, or Graph connectivity failure as a non-blocking Autopilot skip in Foundry Deploy, not as a deployment failure.



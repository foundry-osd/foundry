# Autopilot Hardware Hash Upload - Implementation Phases

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

## Phased Implementation

### Phase 0: Foundation Branch And Research
PR title: `docs(autopilot): plan hardware hash upload from WinPE`

Implementation progress:
- [x] Foundation worktree created.
- [x] Foundation branch created.
- [x] Planning documentation committed.
- [x] Foundation branch pushed.
- [ ] Foundation PR opened.
- [ ] Foundation PR reviewed and merged.

- [x] Create dedicated worktree.
- [x] Create foundation branch.
- [x] Analyze supplied feasibility document.
- [x] Analyze supplied WinPE upload script.
- [x] Query current Microsoft Graph and MSAL documentation through Context7.
- [x] Record the baseline test command without running full solution tests during planning.

Manual checks:
- [x] Confirm branch is isolated from `main`.
- [x] Confirm baseline test command is known: `dotnet test .\src\Foundry.slnx --configuration Debug /p:Platform=x64`.

### Phase 1: Configuration Model
PR title: `feat(autopilot): add provisioning mode configuration`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Add `AutopilotProvisioningMode`.
- [x] Extend `AutopilotSettings` with mode and hardware hash upload settings.
- [x] Extend `DeployAutopilotSettings` with reduced runtime mode and upload settings.
- [x] Add active certificate metadata: Graph `keyId`, thumbprint, expiration, and display name.
- [x] Add tenant app registration identity, service principal identity, known group tags, and default group tag settings.
- [x] Update schema version handling if needed.
- [x] Keep old configurations backward compatible as JSON profile mode.
- [x] Update sanitization in `FoundryConfigurationStateService`.
- [x] Update `DeployConfigurationGenerator`.
- [x] Add XML documentation comments to new public configuration records, enums, and service contracts when they clarify the behavior.

Automated tests:
- [x] Existing JSON profile config serializes and generates the same deploy output.
- [x] Enabled JSON mode requires a selected profile.
- [x] Enabled hash upload mode does not require a selected profile.
- [x] Capture-and-upload mode requires tenant ID, application object ID, client ID, active certificate `keyId`, active certificate thumbprint, and unexpired certificate metadata.
- [x] Invalid certificate settings make Autopilot configuration not ready.
- [x] Expired certificate settings make OSD media generation not ready for hardware hash upload.
- [x] Persistent OSD settings never serialize PFX bytes, PFX password, decrypted private key material, or access tokens.

Manual checks:
- [ ] Deferred to the Phase 3 UI validation pass: start Foundry with existing user config and confirm JSON profile mode is selected.
- [ ] Deferred to the Phase 3 UI validation pass: disable Autopilot and confirm no profile or hash settings are required.

### Phase 2: Security And Tenant Onboarding
PR title: `feat(autopilot): add secure tenant upload onboarding`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Define the permission matrix for the implementation model; user-facing documentation is handled in Phase 8.
- [x] Define tenant/app registration guidance for the OSD onboarding UX and Phase 8 documentation.
- [x] Document the two-app model: official Foundry bootstrap public client for interactive OSD sign-in, tenant-local managed app for WinPE certificate auth.
- [x] Keep the official Foundry bootstrap public client ID fixed for official builds and overrideable only for private builds or forks.
- [x] Implement managed app registration discovery/creation with display name `Foundry OSD Autopilot Registration`.
- [x] Persist tenant ID, application object ID, client ID, service principal object ID, active certificate `keyId`, active certificate thumbprint, and certificate expiration.
- [x] Implement required Graph permission checks and admin consent status checks.
- [x] Implement service principal presence/enabled checks.
- [x] Implement active certificate lifecycle management against Microsoft Graph `keyCredentials`.
- [x] Merge new certificate credentials with the existing `keyCredentials` collection and never prune unknown credentials automatically.
- [x] Implement repair/adoption state for existing display-name matches, missing active certificate credentials, and multiple Foundry-looking credentials without a persisted active certificate.
- [x] Accept only password-protected PFX material for media generation.
- [x] Require a PFX output path during certificate creation.
- [x] Keep created PFX bytes and password in memory only for the current app session.
- [x] Do not implement a ProgramData PFX vault or "remember this PFX" option.
- [x] Validate the PFX leaf certificate thumbprint against the configured active certificate thumbprint.
- [x] Define certificate app-only auth as the only supported WinPE Graph authentication path for code, XML documentation comments, and Phase 8 documentation.
- [x] Define generated media containing encrypted certificate private key material as tenant-sensitive for code warnings, UI copy, and Phase 8 documentation.
- [x] Generalize the existing Foundry Connect AES-GCM media secret envelope for Autopilot secrets.
- [x] Define device code flow, client secrets, and brokered upload as unsupported WinPE authentication modes.
- [x] Define unsupported secret embedding patterns and add test coverage for them.
- [x] Add audit-safe logging rules.
- [x] Add XML documentation comments to new public tenant onboarding, certificate, and secret-protection APIs.
- [x] Reuse the JSON profile tenant download modal sign-in pattern for hardware hash tenant onboarding.
- [x] Route JSON profile download and hardware hash tenant onboarding through one shared tenant operation dialog service.
- [x] Remove the obsolete JSON-specific tenant download dialog wrapper API.
- [x] Make tenant operation cancellation return control to the Autopilot page so the connect action can be retried.
- [x] Reuse the current-session hardware hash Microsoft Graph credential for certificate creation and removal instead of reopening interactive sign-in.
- [x] Clear the current-session hardware hash Microsoft Graph credential when the operator disconnects the tenant.
- [x] Include `User.Read` in the hardware hash onboarding token scopes so Graph organization discovery can read the signed-in tenant ID.
- [x] Keep tenant-dependent OSD UI session-gated: persisted tenant metadata stays stored, but app registration, certificate, group tag, and tenant detail rows stay hidden until a successful current-session tenant connection.
- [x] Show connected tenant details in a dedicated row instead of embedding the tenant ID in the connection row.
- [x] Display tenant details as a table with tenant ID and client ID.
- [x] Add descriptions to Autopilot settings cards so users understand each configuration row.
- [x] Replace the connect action with a disconnect action after successful current-session tenant connection.
- [x] Clear stale persisted active certificate metadata when Microsoft Graph no longer returns the selected active certificate.
- [x] Split certificate management into a certificate action row and a provisioned certificates table row.
- [x] List app registration certificate credentials in a selectable table with thumbprint, creation date, expiration date, and Graph certificate ID.
- [x] Show an empty-state message in the provisioned certificates table row when the tenant app registration has no certificate credentials.
- [x] Do not display an empty-certificate warning when the app registration has no certificate credentials.
- [x] Allow multiple app registration certificates to coexist in the tenant instead of replacing the previously selected certificate during creation.
- [x] Resolve the boot media certificate automatically by matching the selected PFX thumbprint against tenant app registration certificates.
- [x] Move certificate action buttons above the certificate table.
- [x] Remove the visible certificate validity field label while keeping the validity duration selector.
- [x] Remove the redundant active certificate "valid until" text when the same expiration is already visible in the certificate table.
- [x] Remove one or more selected certificate credentials while preserving unrelated app credentials.
- [x] Use WinUI signal brushes for certificate validity: success when valid, caution when expiring within 30 days, and critical when expired.
- [x] Add padding to the certificate expiration table cell so the validity text aligns with the other columns.
- [x] Show the generated PFX password in a selectable read-only field in the one-time certificate-created dialog.
- [x] Enforce Graph application certificate validity limit by offering 1, 3, 6, and 12 months only, with 6 months selected by default.
- [x] Add a dedicated boot media certificate row for selecting the local password-protected PFX and entering its password.
- [x] Automatically fill the boot media certificate row in the current app session after Foundry creates a new certificate.
- [x] Keep boot media PFX path, password, and validation result session-only and excluded from ProgramData serialization.
- [x] Preserve the boot media certificate ready message across Autopilot page navigation when the current-session PFX is still validated.
- [x] Refresh only boot media certificate status while typing the PFX password so tenant detail tables do not rebind on every keystroke.
- [x] Preserve current-session tenant connection, certificate table, onboarding status, and boot media PFX state across page navigation without persisting them across app restart.
- [x] Show onboarding status as compact `Ready` or `Not ready` text with WinUI signal color.
- [x] Remove obsolete verbose onboarding status resource strings after moving detailed remediation to dialogs and readiness blockers.
- [x] Add detailed Autopilot validation codes and Start page messages for hardware hash media generation blockers.
- [x] Discover available group tags from the unfiltered `deviceManagement/windowsAutopilotDeviceIdentities` Graph endpoint and extract `groupTag` client-side.
- [x] Select the optional default group tag from a ComboBox populated by `None` and discovered tenant group tags.
- [x] Keep `None` selected by default because hardware hash upload does not require a group tag.
- [x] Populate the default group tag ComboBox from discovered tenant group tags without displaying a duplicate available group tag table.
- [x] Present the optional group tag configuration as one compact `Default group tag` row.
- [x] Remove obsolete certificate validity/status UI resources after moving readiness to onboarding status, certificate table colors, and boot media PFX validation.

Automated tests:
- [x] App registration discovery uses persisted application object ID before display name.
- [x] Same display name without persisted object ID enters repair/adoption state.
- [x] Required permission missing maps to `PermissionMissing`.
- [x] Admin consent missing maps to `ConsentMissing`.
- [x] Disabled or missing service principal maps to `ServicePrincipalUnavailable`.
- [x] Adding a certificate preserves existing non-active `keyCredentials`.
- [x] App registrations with existing Foundry certificate credentials are tenant-ready without requiring a manual active certificate selection.
- [x] PFX validation can read certificate metadata without a preselected expected thumbprint.
- [x] Retiring a certificate removes only the persisted active `keyId`.
- [x] Created PFX material is not persisted in ProgramData, even with DPAPI.
- [ ] After app restart, media generation requires the operator to select the PFX again and enter its password.
- [x] PFX thumbprint mismatch blocks media generation.
- [x] Secret settings are never serialized into plain deploy config.
- [x] Tampered encrypted certificate envelopes fail without leaking ciphertext, private key material, or certificate password data.
- [ ] Logs redact tokens, secrets, private key paths, certificate data, PFX bytes, and PFX password.
- [x] Foundry OSD build passes after tenant onboarding UX refinements.
- [x] Autopilot targeted tests pass after tenant onboarding UX refinements.

Manual checks:
- [ ] Create the managed app registration in a clean test tenant.
- [ ] Confirm the app registration name is `Foundry OSD Autopilot Registration`.
- [ ] Confirm `Connect tenant` creates an Enterprise application for the official `Foundry OSD` bootstrap client ID `83eb3a92-030d-49b7-881b-32a1eb3e110a` in the target tenant.
- [ ] Confirm required API permissions and admin consent status are visible in Foundry OSD.
- [ ] Add a second certificate credential outside Foundry and confirm Foundry leaves it untouched.
- [ ] Create multiple Foundry certificates and confirm new certificate creation does not remove existing certificates.
- [ ] Create a certificate, choose a PFX output path, and confirm the PFX exists only at the selected path.
- [ ] Restart Foundry OSD and confirm it requires selecting the PFX again before media generation.
- [ ] Review generated media contents and confirm certificate private key material is envelope-encrypted, not plaintext.
- [ ] Review logs after failed auth and successful auth.
- [ ] Confirm least-privilege app registration can import devices.
- [ ] Start Foundry OSD with persisted tenant metadata and confirm only `Tenant connection`, `Not connected`, and `Connect tenant` are shown before current-session sign-in.
- [ ] Click `Connect tenant`, cancel the tenant sign-in dialog, and confirm the Autopilot page remains responsive and `Connect tenant` can be clicked again.
- [ ] Click JSON profile `Download from tenant`, cancel the tenant sign-in dialog, and confirm the JSON profile actions remain responsive.
- [ ] Connect to the tenant and confirm app registration, tenant details, onboarding status, certificate table, and default group tag selection become visible.
- [ ] After connecting once, create and remove certificates and confirm the browser sign-in prompt does not reopen during the same app session.
- [ ] Confirm `Tenant connection` shows only `Connected` or `Not connected`, and the tenant ID appears only in the dedicated tenant details row.
- [ ] Confirm tenant details show tenant ID and client ID in a table after connecting.
- [ ] Confirm each Autopilot settings card has a concise description.
- [ ] Confirm `Onboarding status` displays only `Ready` in success color or `Not ready` in critical color.
- [ ] After connecting, confirm the action changes to `Disconnect tenant` and disconnecting hides tenant-dependent rows without deleting persisted configuration.
- [ ] Connect to a tenant where the persisted active certificate no longer exists in Graph and confirm Foundry clears stale active certificate metadata instead of showing a valid expiration.
- [ ] Connect to an app registration with no certificate credentials and confirm no empty-certificate warning text is displayed.
- [ ] Connect to an app registration with no certificate credentials and confirm the provisioned certificates row shows the empty-state message.
- [ ] Create a certificate and confirm the generated PFX password is selectable/copyable in the content dialog.
- [ ] Create a second certificate and confirm the previous certificate remains present in the tenant certificate table.
- [ ] Confirm the boot media certificate row is automatically filled after certificate creation and returns to empty after app restart.
- [ ] Select each generated PFX with its password and confirm Foundry automatically resolves the matching tenant certificate before reaching the ready state.
- [ ] Select a mismatched PFX and confirm the boot media certificate row shows a thumbprint mismatch.
- [ ] Navigate away from the Autopilot page and back; confirm the tenant remains connected and tenant-dependent rows remain visible.
- [ ] Restart Foundry OSD and confirm the tenant connection returns to the disconnected prompt.
- [ ] In hardware hash mode with no selected boot media PFX, confirm the Start page shows the missing PFX blocker instead of the JSON profile blocker.
- [ ] In hardware hash mode with a mismatched PFX, confirm the Start page shows the thumbprint mismatch blocker.
- [ ] Confirm the certificate table shows thumbprint, creation date, expiration date, and certificate ID with the expected validity color.
- [ ] Confirm `Certificate actions` contains only certificate validity, create certificate, and remove certificate controls.
- [ ] Confirm `Provisioned certificates` contains only the certificate empty state or certificate table.
- [ ] Confirm the certificate validity duration selector no longer shows a visible `Validity` label.
- [ ] Confirm the certificate action buttons are shown above the certificate table.
- [ ] Confirm the redundant active certificate "valid until" text is not shown when the same expiration is already visible in the certificate table.
- [ ] Confirm the certificate expiration column text has the same left padding as the other certificate columns.
- [ ] Confirm the remove certificate action is disabled when no certificate row is selected.
- [ ] Select one or more certificate rows and remove them; confirm only the selected credentials are removed from Entra and the table refreshes.
- [ ] Connect to a tenant with existing Autopilot device group tags and confirm they appear in the `Default group tag` ComboBox without a duplicate available group tag table.
- [ ] Confirm the optional group tag area is one compact `Default group tag` row.
- [ ] Confirm the default group tag ComboBox selects `None` by default.
- [ ] Select a default group tag from the ComboBox and confirm it is saved in the Foundry configuration, then select `None` and confirm the setting is cleared.
- [ ] Create a certificate, navigate away from Autopilot and back, and confirm the boot media certificate row still shows `Certificate ready for boot media generation.`

### Phase 3: Autopilot Page UX
PR title: `feat(autopilot): add hardware hash upload UX`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Replace single Autopilot action section with two settings expanders.
- [x] Keep global Autopilot toggle.
- [x] Move existing import/download/remove/default profile/table UI into JSON profile expander.
- [x] Add hardware hash upload expander.
- [x] Add tenant connection state, connect action, and connected tenant summary.
- [x] Add managed app registration creation/reuse status for `Foundry OSD Autopilot Registration`.
- [x] Add active certificate lifecycle controls: create, remove selected certificate, expired state, missing state, and repair/adoption state.
- [x] Add certificate validity selection with a default of 6 months and options for 1, 3, 6, and 12 months.
- [x] Add one-time private key/PFX content dialog after certificate creation with selectable password text.
- [x] Add password-protected PFX and PFX password input near the active certificate status for boot image generation.
- [x] Add tenant-discovered Autopilot group tag list and default group tag selection.
- [x] Enforce mutual exclusivity between JSON profile and hash upload modes.
- [x] Carry the selected mode into the current Foundry Deploy target page so hardware hash mode does not require a JSON profile.
- [x] Block live hardware hash deployments until the deployment runtime phase exists, instead of silently skipping Autopilot.
- [x] Add localized strings in English and French resources.
- [x] Update readiness messages to include selected mode.
- [ ] Add XML documentation comments to new public view-model members or UI service contracts when the behavior is not obvious.

Automated tests:
- [x] View model mode changes save state.
- [x] Selecting JSON mode preserves hash upload metadata.
- [x] Selecting hash upload mode preserves hash upload metadata and does not require a selected JSON profile in the OSD UI.
- [x] Deploy launch preparation accepts hardware hash mode without a selected JSON profile.
- [x] Current Deploy Autopilot staging step skips JSON profile staging in hardware hash mode.
- [x] Live hardware hash mode fails before deployment confirmation until the runtime implementation exists.
- [x] Hardware hash media generation is not ready when the connected app certificate is expired.
- [x] Hardware hash media generation requires a password-protected PFX whose leaf certificate thumbprint matches the active certificate.
- [x] Creating a certificate exposes the private key/PFX material once and never persists the raw PFX, password, or decrypted private key.
- [ ] Busy state still blocks JSON profile import/download/remove commands.

Manual checks:
- [ ] Autopilot disabled: both expanders are unavailable or collapsed according to final UX decision.
- [ ] Autopilot enabled: both expanders are visible.
- [ ] Activating one method deactivates the other.
- [ ] JSON profile import and tenant download still work.
- [ ] Connect to a tenant with no app registration and confirm Foundry OSD creates `Foundry OSD Autopilot Registration`.
- [ ] Connect to a tenant with an existing managed app registration and confirm Foundry OSD reuses it.
- [ ] Connect to a tenant where an app with the same display name exists but no persisted Foundry app ID exists, and confirm Foundry OSD enters repair/adoption state.
- [ ] Create a certificate, verify the private key/PFX material and password are shown once, close the dialog, and confirm they cannot be shown again.
- [ ] Confirm the boot media certificate row shows the generated PFX path and password as ready in the same app session.
- [ ] Restart Foundry OSD and confirm the boot media certificate row requires selecting the PFX and entering the password again.
- [ ] Add an extra non-active certificate credential to the app and confirm Foundry OSD warns but does not delete or block on it.
- [ ] Expire or simulate an expired certificate and confirm the OSD page clearly requires regenerating the certificate before boot image creation.

### Phase 4: Media Build And WinPE Assets
PR title: `feat(winpe): stage autopilot hash capture assets`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Add `WinPE-SecureStartup` to the default required optional components for all generated WinPE media.
- [ ] Locate and stage architecture-specific `oa3tool.exe` from the ADK for x64 and ARM64.
- [ ] Add hash capture templates under a Foundry-owned WinPE path.
- [ ] Add hash upload runtime configuration under `X:\Foundry\Config`.
- [ ] Write encrypted Autopilot PFX and PFX password envelopes plus the media secret key through the shared media secret provisioning path.
- [ ] Keep current profile JSON staging unchanged in JSON profile mode.
- [ ] Do not stage JSON profile folders in hash upload mode unless the user also keeps profiles for another purpose.
- [ ] Do not stage `PCPKsp.dll` during media build.
- [ ] Add XML documentation comments to new public media asset provisioning APIs and secret envelope APIs.

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
- [ ] Confirm no plaintext PFX, PFX password, private key, token, or client secret is written to media.

### Phase 5: Foundry Deploy Runtime Branching
PR title: `feat(deploy): branch autopilot runtime by provisioning mode`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Load Autopilot provisioning mode from deploy config.
- [ ] Expose mode in startup snapshot, preparation view model, launch request, deployment context, and runtime state.
- [ ] Expose hardware hash group tag selection mode in the Computer Target page.
- [ ] Update `DeploymentLaunchPreparationService` validation:
  - JSON mode requires selected profile.
  - Hash upload mode requires valid upload settings.
- [ ] Rename or replace `StageAutopilotConfigurationStep` with a mode-aware `ProvisionAutopilotStep`.
- [ ] Update `DeploymentStepNames.All`, dependency injection registration, and sequence validation tests together when the Autopilot step is renamed or replaced.
- [ ] Keep the Autopilot provisioning step after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- [ ] JSON mode copies `AutopilotConfigurationFile.json` from the mode-aware Autopilot provisioning step.
- [ ] Hash upload mode skips JSON staging and runs the hash capture/upload workflow from the same late Autopilot provisioning step.
- [ ] Update deployment summary, logs, and telemetry with mode, planned hash-upload status, and retained diagnostics path.
- [ ] Add runtime status states for hash upload warnings, skipped states, and later Graph import outcomes without requiring a live Graph call in this phase.
- [ ] Persist sanitized Autopilot diagnostics under `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash`.
- [ ] Add XML documentation comments to new public deployment runtime contracts and step classes.

Automated tests:
- [ ] JSON mode still stages the profile to `Windows\Provisioning\Autopilot`.
- [ ] Hash upload mode skips JSON staging.
- [ ] Dry run creates a hash-mode manifest without touching Graph.
- [ ] Autopilot provisioning step is ordered after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- [ ] Hash upload mode runs only after the applied Windows root and target Windows `System32` are available.
- [ ] Launch preparation rejects incomplete hash upload settings.
- [ ] Expired certificate state hides hardware hash group tag controls and leaves deployment start available.
- [ ] Runtime state can represent a skipped Autopilot hash upload without failing the deployment state machine.

Manual checks:
- [ ] Deploy dry-run in JSON mode.
- [ ] Deploy dry-run in hash upload mode.
- [ ] Confirm summary page displays the selected Autopilot method.
- [ ] In hash mode, confirm Computer Target shows only hardware hash controls.
- [ ] In JSON mode, confirm Computer Target shows only JSON profile controls.
- [ ] In hash mode with expired certificate, confirm Deploy shows the regeneration/recreate media message and still allows OS deployment.
- [ ] Confirm logs contain mode, hash capture diagnostics path, and upload state.

### Phase 6: Hash Capture Service
PR title: `feat(deploy): capture autopilot hardware hash in WinPE`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Add a C# service that runs OA3Tool with controlled working directory paths.
- [ ] Add a C# service that copies `PCPKsp.dll` from `<target Windows>\Windows\System32` to `X:\Windows\System32`.
- [ ] Validate source and destination architecture assumptions for x64 and ARM64.
- [ ] Generate `OA3.cfg` and dummy input XML internally.
- [ ] Validate `OA3.xml` exists.
- [ ] Extract serial number and hardware hash.
- [ ] Write a local CSV artifact for troubleshooting.
- [ ] Preserve OA3 logs in Foundry deployment logs.
- [ ] Return structured failure codes for missing tool, `PCPKsp.dll` copy/load failure, empty hash, invalid XML, missing serial, and OA3 exit failure.
- [ ] Add XML documentation comments to new public hash capture, OA3Tool, parser, and artifact writer APIs.

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
- [ ] Confirm troubleshooting files are retained under `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash`.
- [ ] Confirm retained files do not contain tokens, PFX bytes, PFX password, decrypted private key material, encrypted secret blobs, or raw Graph payloads.

### Phase 7: Graph Upload Service
PR title: `feat(autopilot): import hardware hashes with Graph`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Add a minimal Graph Autopilot import client.
- [ ] Add certificate-based credential creation from decrypted in-memory certificate material.
- [ ] Reject any non-certificate authentication mode in WinPE.
- [ ] Implement import request.
- [ ] Implement polling for import completion.
- [ ] Implement polling until the uploaded serial number appears in Windows Autopilot devices.
- [ ] Add a 10-minute default timeout for Windows Autopilot device visibility polling.
- [ ] Map Graph errors to operator-readable messages.
- [ ] Add retry/backoff for transient HTTP failures.
- [ ] Treat certificate, tenant, token, consent, permission, Conditional Access, Intune availability, Graph connectivity, `ImportFailed`, duplicate import, and `ImportTimedOut` states as non-blocking Autopilot failures that continue OS deployment.
- [ ] Keep destructive cleanup out of the final hash upload workflow.
- [ ] Sanitize `AutopilotUploadResult.json` before retaining it in `Windows\Temp\Foundry`.
- [ ] Add XML documentation comments to new public Graph client, import polling, and retry-policy APIs.

Automated tests:
- [ ] Serializes import payload correctly.
- [ ] Sends hardware identifier in the expected Graph format.
- [ ] Decrypts PFX material in memory and does not write a decrypted PFX, PFX password, or private key to disk.
- [ ] Fails clearly when tenant ID, client ID, certificate thumbprint, or encrypted certificate material is missing.
- [ ] Treats certificate, tenant, token, permission, consent, Conditional Access, Intune availability, and Graph connectivity failures as skipped Autopilot, not failed deployment.
- [ ] Treats duplicate import errors, `ImportFailed`, and `ImportTimedOut` as Autopilot warnings/failures that do not stop OS deployment.
- [ ] Handles `complete`.
- [ ] Handles imported identity completion followed by Windows Autopilot device visibility.
- [ ] Handles Windows Autopilot device visibility timeout as an automatic warning/non-blocking continuation to the next deployment step.
- [ ] Handles `error` with device error code/name.
- [ ] Times out with a clear message.
- [ ] Retries transient failures only.
- [ ] Sanitized upload result omits access tokens, authorization headers, raw request bodies, raw response bodies, PFX bytes, passwords, private key material, and full certificate data.

Manual checks:
- [ ] Import one test device into a test tenant.
- [ ] Confirm Group Tag appears in Intune.
- [ ] Confirm deployment waits until the device appears in Windows Autopilot devices.
- [ ] Confirm the wait shows an indeterminate sub-progress indicator and countdown.
- [ ] Confirm a 10-minute visibility timeout automatically continues OS deployment and records a warning.
- [ ] Confirm assignment sync behavior is documented, even if not waited on by the final implementation.
- [ ] Confirm duplicate device behavior is clear to the operator.
- [ ] Confirm an existing duplicate device import error is surfaced clearly and does not trigger automatic cleanup.
- [ ] In hash mode with simulated auth failure, confirm Deploy shows an Autopilot warning and still continues OS deployment.

### Phase 8: Documentation And Release Guardrails
PR title: `docs(autopilot): document WinPE hardware hash upload`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Docusaurus worktree and branch created.
- [ ] Implementation checklist complete.
- [ ] Documentation build or preview complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] Foundry PR opened with the planned title.
- [ ] Docusaurus PR opened with the planned title.
- [ ] Foundry and Docusaurus PRs merged.

- [ ] Add user documentation for hardware hash upload from WinPE.
- [ ] Update the Docusaurus documentation if the implemented behavior affects user-facing OSD, Deploy, WinPE requirements, setup, troubleshooting, permissions, or release notes.
- [ ] Use the Docusaurus repository at `E:\Github\Foundry Project\foundry-osd.github.io`.
- [ ] Create a dedicated Docusaurus worktree before editing docs.
- [ ] Create the Docusaurus branch `docs/autopilot-hash-upload` from the current documentation base branch.
- [ ] Use the Docusaurus PR title `docs(autopilot): document WinPE hardware hash upload`.
- [ ] Locate the Docusaurus documentation source inside that repository by searching for `docusaurus.config.*` or the docs package root before editing docs.
- [ ] Mark WinPE hash capture as best-effort and not the Microsoft-standard method.
- [ ] Document x64 and ARM64 scope.
- [ ] Document that Foundry copies `PCPKsp.dll` from the applied OS to `X:\Windows\System32` late in deployment.
- [ ] Document network requirements for Ethernet and Wi-Fi.
- [ ] Document retained troubleshooting artifacts under `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash` and the operator cleanup process after diagnostics are no longer needed.
- [ ] Document unsupported or risky scenarios:
  - self-deploying mode
  - pre-provisioning
  - missing TPM visibility
- [ ] Update screenshots after UI implementation.
- [ ] Update Docusaurus navigation/sidebar entries if a new Autopilot hardware hash page is added.

Manual checks:
- [ ] Follow the docs on a clean test tenant.
- [ ] Follow the docs on a clean x64 test device.
- [ ] Follow the docs on a clean ARM64 test device.
- [ ] Confirm fallback to OOBE/full OS instructions are clear.
- [ ] Build or preview the Docusaurus docs with the command discovered from the docs package scripts if Docusaurus files are changed.

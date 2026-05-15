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
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Add `AutopilotProvisioningMode`.
- [ ] Extend `AutopilotSettings` with mode and hardware hash upload settings.
- [ ] Extend `DeployAutopilotSettings` with reduced runtime mode and upload settings.
- [ ] Add active certificate metadata: Graph `keyId`, thumbprint, expiration, and display name.
- [ ] Add tenant app registration identity, service principal identity, known group tags, and default group tag settings.
- [ ] Update schema version handling if needed.
- [ ] Keep old configurations backward compatible as JSON profile mode.
- [ ] Update sanitization in `ExpertDeployConfigurationStateService`.
- [ ] Update `DeployConfigurationGenerator`.
- [ ] Add XML documentation comments to new public configuration records, enums, and service contracts when they clarify the behavior.

Automated tests:
- [ ] Existing JSON profile config serializes and generates the same deploy output.
- [ ] Enabled JSON mode requires a selected profile.
- [ ] Enabled hash upload mode does not require a selected profile.
- [ ] Capture-and-upload mode requires tenant ID, application object ID, client ID, active certificate `keyId`, active certificate thumbprint, and unexpired certificate metadata.
- [ ] Invalid certificate settings make Autopilot configuration not ready.
- [ ] Expired certificate settings make OSD media generation not ready for hardware hash upload.
- [ ] Persistent OSD settings never serialize PFX bytes, PFX password, decrypted private key material, or access tokens.

Manual checks:
- [ ] Start Foundry with existing user config and confirm JSON profile mode is selected.
- [ ] Disable Autopilot and confirm no profile or hash settings are required.

### Phase 2: Security And Tenant Onboarding
PR title: `feat(autopilot): add secure tenant upload onboarding`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Define the permission matrix for the implementation model; user-facing documentation is handled in Phase 8.
- [ ] Define tenant/app registration guidance for the OSD onboarding UX and Phase 8 documentation.
- [ ] Implement managed app registration discovery/creation with display name `Foundry OSD Autopilot Registration`.
- [ ] Persist tenant ID, application object ID, client ID, service principal object ID, active certificate `keyId`, active certificate thumbprint, and certificate expiration.
- [ ] Implement required Graph permission checks and admin consent status checks.
- [ ] Implement service principal presence/enabled checks.
- [ ] Implement active certificate lifecycle management against Microsoft Graph `keyCredentials`.
- [ ] Merge new certificate credentials with the existing `keyCredentials` collection and never prune unknown credentials automatically.
- [ ] Implement repair/adoption state for existing display-name matches, missing active certificate credentials, and multiple Foundry-looking credentials without a persisted active certificate.
- [ ] Accept only password-protected PFX material for media generation.
- [ ] Require a PFX output path during certificate creation.
- [ ] Keep created PFX bytes and password in memory only for the current app session.
- [ ] Do not implement a ProgramData PFX vault or "remember this PFX" option.
- [ ] Validate the PFX leaf certificate thumbprint against the configured active certificate thumbprint.
- [ ] Define certificate app-only auth as the only supported WinPE Graph authentication path for code, XML documentation comments, and Phase 8 documentation.
- [ ] Define generated media containing encrypted certificate private key material as tenant-sensitive for code warnings, UI copy, and Phase 8 documentation.
- [ ] Generalize the existing Foundry Connect AES-GCM media secret envelope for Autopilot secrets.
- [ ] Define device code flow, client secrets, and brokered upload as unsupported WinPE authentication modes.
- [ ] Define unsupported secret embedding patterns and add test coverage for them.
- [ ] Add audit-safe logging rules.
- [ ] Add XML documentation comments to new public tenant onboarding, certificate, and secret-protection APIs.

Automated tests:
- [ ] App registration discovery uses persisted application object ID before display name.
- [ ] Same display name without persisted object ID enters repair/adoption state.
- [ ] Required permission missing maps to `PermissionMissing`.
- [ ] Admin consent missing maps to `ConsentMissing`.
- [ ] Disabled or missing service principal maps to `ServicePrincipalUnavailable`.
- [ ] Adding a certificate preserves existing non-active `keyCredentials`.
- [ ] Retiring a certificate removes only the persisted active `keyId`.
- [ ] Created PFX material is not persisted in ProgramData, even with DPAPI.
- [ ] After app restart, media generation requires the operator to select the PFX again and enter its password.
- [ ] PFX thumbprint mismatch blocks media generation.
- [ ] Secret settings are never serialized into plain deploy config.
- [ ] Tampered encrypted certificate envelopes fail without leaking ciphertext, private key material, or certificate password data.
- [ ] Logs redact tokens, secrets, private key paths, certificate data, PFX bytes, and PFX password.

Manual checks:
- [ ] Create the managed app registration in a clean test tenant.
- [ ] Confirm the app registration name is `Foundry OSD Autopilot Registration`.
- [ ] Confirm required API permissions and admin consent status are visible in Foundry OSD.
- [ ] Add a second certificate credential outside Foundry and confirm Foundry leaves it untouched.
- [ ] Replace the active certificate and confirm the old credential is retained until the operator explicitly retires it.
- [ ] Create a certificate, choose a PFX output path, and confirm the PFX exists only at the selected path.
- [ ] Restart Foundry OSD and confirm it requires selecting the PFX again before media generation.
- [ ] Review generated media contents and confirm certificate private key material is envelope-encrypted, not plaintext.
- [ ] Review logs after failed auth and successful auth.
- [ ] Confirm least-privilege app registration can import devices.

### Phase 3: Autopilot Page UX
PR title: `feat(autopilot): add provisioning method selection`

Implementation progress:
- [ ] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [ ] Implementation checklist complete.
- [ ] Automated tests complete.
- [ ] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [ ] Replace single Autopilot action section with two settings expanders.
- [ ] Keep global Autopilot toggle.
- [ ] Move existing import/download/remove/default profile/table UI into JSON profile expander.
- [ ] Add hardware hash upload expander.
- [ ] Add tenant connection state, connect action, and connected tenant summary.
- [ ] Add managed app registration creation/reuse status for `Foundry OSD Autopilot Registration`.
- [ ] Add active certificate lifecycle controls: create, retire, replace, expired state, missing state, and repair/adoption state.
- [ ] Add certificate validity selection with a default of 12 months.
- [ ] Add one-time private key/PFX content dialog after certificate creation.
- [ ] Add password-protected PFX and PFX password input near the active certificate status for boot image generation.
- [ ] Add tenant-discovered Autopilot group tag list and default group tag selection.
- [ ] Enforce mutual exclusivity between JSON profile and hash upload modes.
- [ ] Add localized strings in English and French resources.
- [ ] Update readiness messages to include selected mode.
- [ ] Add XML documentation comments to new public view-model members or UI service contracts when the behavior is not obvious.

Automated tests:
- [ ] View model mode changes save state.
- [ ] Selecting JSON mode disables hash upload readiness requirements.
- [ ] Selecting hash upload mode disables JSON profile selection requirements.
- [ ] Hardware hash media generation is not ready when the connected app certificate is expired.
- [ ] Hardware hash media generation requires a password-protected PFX whose leaf certificate thumbprint matches the active certificate.
- [ ] Creating a certificate exposes the private key/PFX material once and never persists the raw PFX, password, or decrypted private key.
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



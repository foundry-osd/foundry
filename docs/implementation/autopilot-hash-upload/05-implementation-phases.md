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
- [x] Foundation PR opened.
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
- [x] PR merged back into `feature/autopilot-hash-upload-foundation`.

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
- [x] Deferred to the Phase 3 UI validation pass: start Foundry with existing user config and confirm JSON profile mode is selected.
- [x] Deferred to the Phase 3 UI validation pass: disable Autopilot and confirm no profile or hash settings are required.

### Phase 2: Security And Tenant Onboarding
PR title: `feat(autopilot): add secure tenant upload onboarding`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [x] PR merged back into `feature/autopilot-hash-upload-foundation`.

Scope note:
- Phase 2 went beyond the original security-only scope and pulled forward most of the OSD hardware hash UX planned for Phase 3.
- Future phases must not reimplement tenant connection, tenant readiness, certificate management, boot media PFX validation, default group tag discovery/selection, or Start page hardware hash readiness blockers unless a later review explicitly changes the UX.

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
- [x] Keep interactive Microsoft Graph authentication session-only by disabling persistent MSAL token cache storage for OSD tenant operations.
- [x] Include `User.Read` in the hardware hash onboarding token scopes so Graph organization discovery can read the signed-in tenant ID.
- [x] Keep tenant-dependent OSD UI session-gated: persisted tenant metadata stays stored, but tenant readiness, certificate, and group tag rows stay hidden until a successful current-session tenant connection.
- [x] Show tenant readiness in one dedicated row instead of embedding tenant and readiness details in separate rows.
- [x] Display managed app registration state, tenant ID, client ID, and readiness status in a compact read-only table.
- [x] Add descriptions to Autopilot settings cards so users understand each configuration row.
- [x] Replace the connect action with a disconnect action after successful current-session tenant connection.
- [x] Clear stale persisted active certificate metadata when Microsoft Graph no longer returns the selected active certificate.
- [x] Split certificate management into a certificate action row and a provisioned certificates table row.
- [x] List app registration certificate credentials in a selectable table with thumbprint, creation date, expiration date, and Graph certificate ID.
- [x] Show an empty-state message in the provisioned certificates table row when the tenant app registration has no certificate credentials.
- [x] Do not display an empty-certificate warning when the app registration has no certificate credentials.
- [x] Allow multiple app registration certificates to coexist in the tenant instead of replacing the previously selected certificate during creation.
- [x] Filter the provisioned certificate table to Foundry-managed certificate credentials so unrelated app registration credentials are not shown or removable from Foundry.
- [x] Delete the generated local PFX file if Graph certificate upload fails during certificate creation.
- [x] Resolve the boot media certificate automatically by matching the selected PFX thumbprint against tenant app registration certificates.
- [x] Move certificate action buttons above the certificate table.
- [x] Remove the visible certificate validity field label while keeping the validity duration selector.
- [x] Remove the redundant active certificate "valid until" text when the same expiration is already visible in the certificate table.
- [x] Remove one or more selected certificate credentials while preserving unrelated app credentials.
- [x] Use WinUI signal brushes for certificate validity: success when valid, caution when expiring within 30 days, and critical when expired.
- [x] Add padding to the certificate expiration table cell so the validity text aligns with the other columns.
- [x] Show the generated PFX password in a selectable read-only field in the one-time certificate-created dialog.
- [x] Make the certificate-created dialog explicitly tell the operator to save both the PFX file and generated password before closing it.
- [x] Add a copy-to-clipboard action for the generated PFX password in the one-time certificate-created dialog.
- [x] Enforce Graph application certificate validity limit by offering 1, 3, 6, and 12 months only, with 6 months selected by default.
- [x] Add a dedicated boot media certificate row for selecting the local password-protected PFX and entering its password.
- [x] Automatically fill the boot media certificate row in the current app session after Foundry creates a new certificate.
- [x] Keep boot media PFX path, password, and validation result session-only and excluded from ProgramData serialization.
- [x] Preserve the boot media certificate ready message across Autopilot page navigation when the current-session PFX is still validated.
- [x] Refresh only boot media certificate status while typing the PFX password so tenant readiness details do not rebind on every keystroke.
- [x] Prioritize boot media PFX-specific readiness messages over generic active certificate metadata blockers on the Autopilot page and Start page.
- [x] Preserve current-session tenant connection, certificate table, onboarding status, and boot media PFX state across page navigation without persisting them across app restart.
- [x] Show onboarding status as compact `Ready` or `Not ready` text with WinUI signal color.
- [x] Show tenant connection state as `Connected` in success color or `Not connected` in critical color.
- [x] Suppress the tenant onboarding success content dialog; successful connection is shown inline through the readiness table.
- [x] Keep tenant readiness `Ready` when at least one valid Foundry-managed app certificate remains after removing other selected certificates.
- [x] Remove obsolete verbose onboarding status resource strings after moving detailed remediation to dialogs and readiness blockers.
- [x] Add detailed Autopilot validation codes and Start page messages for hardware hash media generation blockers.
- [x] Discover available group tags from the unfiltered `deviceManagement/windowsAutopilotDeviceIdentities` Graph endpoint and extract `groupTag` client-side.
- [x] Preserve the previously saved default group tag if group tag discovery fails during tenant connection.
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
- [x] Covered by manual validation: after app restart, media generation requires the operator to select the PFX again and enter its password.
- [x] PFX thumbprint mismatch blocks media generation.
- [x] Secret settings are never serialized into plain deploy config.
- [x] Tampered encrypted certificate envelopes fail without leaking ciphertext, private key material, or certificate password data.
- [x] Deferred to Phase 8 documentation/release guardrails: logs redact tokens, secrets, private key paths, certificate data, PFX bytes, and PFX password.
- [x] Foundry OSD build passes after tenant onboarding UX refinements.
- [x] Autopilot targeted tests pass after tenant onboarding UX refinements.

Manual checks:
- [x] Create the managed app registration in a clean test tenant.
- [x] Confirm the app registration name is `Foundry OSD Autopilot Registration`.
- [x] Confirm `Connect tenant` creates an Enterprise application for the official `Foundry OSD` bootstrap client ID `83eb3a92-030d-49b7-881b-32a1eb3e110a` in the target tenant.
- [x] Confirm required API permissions and admin consent status are visible in Foundry OSD.
- [x] Add a second certificate credential outside Foundry and confirm Foundry leaves it untouched.
- [x] Create multiple Foundry certificates and confirm new certificate creation does not remove existing certificates.
- [x] Create a certificate, choose a PFX output path, and confirm the PFX exists only at the selected path.
- [x] Restart Foundry OSD and confirm it requires selecting the PFX again before media generation.
- [x] Deferred to Phase 4 media validation: review generated media contents and confirm certificate private key material is envelope-encrypted, not plaintext.
- [x] Deferred to Phase 8 release guardrails: review logs after failed auth and successful auth.
- [x] Deferred to Phase 7 Graph upload validation: confirm least-privilege app registration can import devices.
- [x] Start Foundry OSD with persisted tenant metadata and confirm only `Tenant connection`, `Not connected`, and `Connect tenant` are shown before current-session sign-in.
- [x] Click `Connect tenant`, cancel the tenant sign-in dialog, and confirm the Autopilot page remains responsive and `Connect tenant` can be clicked again.
- [x] Click JSON profile `Download from tenant`, cancel the tenant sign-in dialog, and confirm the JSON profile actions remain responsive.
- [x] Connect to the tenant and confirm tenant readiness, certificate actions, provisioned certificates, boot media certificate, and default group tag selection become visible.
- [x] After connecting once, create and remove certificates and confirm the browser sign-in prompt does not reopen during the same app session.
- [x] Confirm `Tenant connection` shows only `Connected` or `Not connected`, and the tenant ID appears only in the dedicated tenant readiness row.
- [x] Confirm tenant readiness shows managed app registration state, tenant ID, client ID, and readiness status in a compact table after connecting.
- [x] Confirm each Autopilot settings card has a concise description.
- [x] Confirm tenant readiness displays status as only `Ready` in success color or `Not ready` in critical color.
- [x] Confirm tenant connection displays `Connected` in success color or `Not connected` in critical color.
- [x] After connecting, confirm the action changes to `Disconnect tenant` and disconnecting hides tenant-dependent rows without deleting persisted configuration.
- [x] Connect to a tenant where the persisted active certificate no longer exists in Graph and confirm Foundry clears stale active certificate metadata instead of showing a valid expiration.
- [x] Connect to an app registration with no certificate credentials and confirm no empty-certificate warning text is displayed.
- [x] Connect to an app registration with no certificate credentials and confirm the provisioned certificates row shows the empty-state message.
- [x] Create a certificate and confirm the generated PFX password is selectable/copyable in the content dialog.
- [x] Create a certificate and confirm the content dialog clearly tells the operator to save both the PFX file and PFX password before closing it.
- [x] Click `Copy password` in the certificate-created dialog and confirm the generated PFX password is copied to the clipboard.
- [x] Create a second certificate and confirm the previous certificate remains present in the tenant certificate table.
- [x] Confirm the boot media certificate row is automatically filled after certificate creation and returns to empty after app restart.
- [x] Select each generated PFX with its password and confirm Foundry automatically resolves the matching tenant certificate before reaching the ready state.
- [x] Select a mismatched PFX and confirm the boot media certificate row shows a thumbprint mismatch.
- [x] Navigate away from the Autopilot page and back; confirm the tenant remains connected and tenant-dependent rows remain visible.
- [x] Restart Foundry OSD and confirm the tenant connection returns to the disconnected prompt.
- [x] In hardware hash mode with no selected boot media PFX, confirm the Start page shows the missing PFX blocker instead of the JSON profile blocker.
- [x] In hardware hash mode with a mismatched PFX, confirm the Start page shows the thumbprint mismatch blocker.
- [x] Confirm the certificate table shows thumbprint, creation date, expiration date, and certificate ID with the expected validity color.
- [x] Confirm `Certificate actions` contains only certificate validity, create certificate, and remove certificate controls.
- [x] Confirm `Provisioned certificates` contains only the certificate empty state or certificate table.
- [x] Confirm the certificate validity duration selector no longer shows a visible `Validity` label.
- [x] Confirm the certificate action buttons are shown above the certificate table.
- [x] Confirm the redundant active certificate "valid until" text is not shown when the same expiration is already visible in the certificate table.
- [x] Confirm the certificate expiration column text has the same left padding as the other certificate columns.
- [x] Confirm the remove certificate action is disabled when no certificate row is selected.
- [x] Select one or more certificate rows and remove them; confirm only the selected credentials are removed from Entra and the table refreshes.
- [x] Create multiple certificates, remove a subset, and confirm tenant readiness stays `Ready` while at least one valid certificate remains.
- [x] Connect to a ready tenant and confirm no success content dialog is shown.
- [x] Connect to a tenant with existing Autopilot device group tags and confirm they appear in the `Default group tag` ComboBox without a duplicate available group tag table.
- [x] Confirm the optional group tag area is one compact `Default group tag` row.
- [x] Confirm the default group tag ComboBox selects `None`/`Aucun` by default depending on UI language.
- [x] Select a default group tag from the ComboBox and confirm it is saved in the Foundry configuration, then select `None`/`Aucun` and confirm the setting is cleared.
- [x] Create a certificate, navigate away from Autopilot and back, and confirm the boot media certificate row still shows `Certificate ready for boot media generation.`

### Phase 3: Foundry Deploy Autopilot UX
PR title: `feat(deploy): add autopilot hardware hash UX`

Status note:
- Most Phase 3 UX work was completed ahead of schedule during Phase 2 in `feature/autopilot-hash-upload-security`.
- Phase 3 now focuses on Foundry Deploy Autopilot UX only. Foundry OSD Autopilot UX should be treated as already implemented unless a later review explicitly changes it.
- Runtime execution still belongs to Phase 5 and later phases. Phase 3 must not implement OA3Tool execution, Graph certificate authentication, hash import, or device visibility polling.

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Render only the selected Autopilot provisioning mode from the OSD-generated deploy configuration.
- [x] In disabled Autopilot mode, show only the media provisioning mode summary and no provisioning controls.
- [x] Keep JSON profile mode focused on profile selection only.
- [x] Hide JSON profile counts and other non-actionable metadata from the Computer Target page.
- [x] In hardware hash mode, do not show JSON profile selection or JSON staging controls.
- [x] In hardware hash mode, show a compact operator-facing hardware hash upload status on the Computer Target page.
- [x] Show tenant ID, certificate thumbprint, and certificate expiration in hardware hash mode for ready, expired, and missing metadata scenarios.
- [x] If the certificate is valid, show a ready status and a single message that upload will run automatically during deployment.
- [x] If the certificate is expired, show a clear non-blocking message telling the operator to regenerate the certificate and recreate the boot image; continue OS deployment without Autopilot.
- [x] Carry tenant-discovered known group tags into Deploy runtime configuration.
- [x] Add one group tag ComboBox for hardware hash mode, populated by `None` plus embedded known group tags.
- [x] Select the OSD default group tag when it still exists in known group tags; otherwise select `None`.
- [x] Disable or hide group tag controls when certificate authentication cannot be attempted.
- [x] Keep runtime execution details out of the target selection UI; Phase 5 owns execution progress and upload results.
- [x] Update deployment launch preparation UI so hash mode no longer reports a missing JSON profile blocker.
- [x] Carry the selected hardware hash group tag into the deployment launch request and summary.
- [x] Make the current live runtime boundary skip hardware hash upload instead of failing the OS deployment before Phase 5+.
- [x] Move Deploy preview shortcuts for progress, success, and error pages into a debug-only top-level menu.
- [x] Add debug-only Autopilot presets for no Autopilot, JSON profile, valid hardware hash certificate, expired hardware hash certificate, missing hardware hash certificate metadata, and hardware hash without a default group tag.
- [x] Remove obsolete wizard footer debug buttons now that debug commands are centralized under the Debug menu.
- [x] Add localized strings in English and French resources.
- [x] Add XML documentation comments to new public Deploy view-model members or UI service contracts when the behavior is not obvious.

Automated tests:
- [x] Disabled Autopilot mode exposes a compact configured-mode summary.
- [x] Deploy target view model exposes JSON profile controls in JSON mode.
- [x] Deploy target view model exposes hardware hash controls in hash mode.
- [x] Deploy target view model hides JSON profile controls in hash mode.
- [x] JSON profile mode exposes the missing-profile hint only when no embedded profile is available.
- [x] Hash mode does not require a selected JSON profile in launch preparation.
- [x] Valid hardware hash mode exposes a ready upload status and operator-facing upload message.
- [x] Expired certificate state hides hardware hash group tag controls and leaves deployment start available.
- [x] Missing hardware hash certificate key ID keeps hash mode not ready and hides group tag controls.
- [x] Default group tag selection initializes from the OSD-generated configuration when the group tag still exists in known group tags.
- [x] Hash mode without a default group tag selects `None`.
- [x] A selected known group tag overrides the OSD default group tag for the current deployment request.
- [x] If the OSD default group tag no longer exists in known group tags, `None` is selected.
- [x] `None` group tag remains a valid selection and serializes as no group tag.
- [x] Summary state exposes the effective group tag for hash mode.
- [x] Debug Autopilot presets produce the expected launch preparation and certificate readiness states.
- [x] `dotnet test .\src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj` passed: 162 tests, 0 failures.

Manual checks:
Completed through Visual Studio/debug-safe validation before squash. Non-debug menu visibility remains explicitly deferred to a release-build smoke check.

- [x] In disabled Autopilot mode, confirm the Computer Target page shows only the configured media mode summary and no provisioning controls.
- [x] In JSON mode, confirm Foundry Deploy shows only profile selection and no profile count or hash metadata.
- [x] In hardware hash ready mode, confirm Foundry Deploy shows upload status, tenant ID, certificate thumbprint, certificate expiration, one actionable message, and the group tag ComboBox.
- [x] In hash mode with a valid certificate, confirm the group tag ComboBox contains `None` plus embedded known group tags.
- [x] In hash mode with an OSD default group tag that exists in known group tags, confirm that group tag is selected by default.
- [x] In hash mode with an OSD default group tag that no longer exists in known group tags, confirm `None` is selected.
- [x] In hash mode with `None`, confirm no group tag is sent in the deployment request.
- [x] In hash mode with an expired certificate, confirm Deploy shows tenant ID, certificate thumbprint, certificate expiration, the regeneration/recreate media message, hides the group tag ComboBox, and still allows OS deployment.
- [x] In hash mode with missing certificate metadata, confirm Deploy shows tenant ID, unavailable certificate fields, the not-ready message, hides the group tag ComboBox, and still allows OS deployment.
- [x] Confirm hash mode does not show a missing JSON profile blocker.
- [x] Confirm JSON mode behavior and text did not regress.
- [x] Confirm no runtime-pending warning is shown on the Computer Target page.
- [x] In a Visual Studio/debug-safe run, confirm the top-level Debug menu is visible.
- [x] Deferred to release-build smoke check: in a non-debug run, confirm the top-level Debug menu is hidden.
- [x] Confirm Debug > Autopilot > No Autopilot disables Autopilot controls and blockers.
- [x] Confirm Debug > Autopilot > JSON profile shows the JSON/profile Autopilot controls.
- [x] Confirm Debug > Autopilot > Hardware hash upload > Valid certificate shows hash mode as ready.
- [x] Confirm Debug > Autopilot > Hardware hash upload > Expired certificate shows the expired-certificate non-blocking state.
- [x] Confirm Debug > Autopilot > Hardware hash upload > Missing certificate info shows hash mode as not ready.
- [x] Confirm Debug > Autopilot > Hardware hash upload > No default group tag keeps hash mode ready and selects `None`/`Aucun`.
- [x] Confirm Debug > Deployment pages opens the progress, success, and error preview pages.
- [x] Confirm the wizard footer no longer shows debug preview buttons.

### Phase 4: Media Build And WinPE Assets
PR title: `feat(winpe): stage autopilot hash capture assets`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Add `WinPE-SecureStartup` to the default required optional components for all generated WinPE media.
- [x] Locate and stage architecture-specific `oa3tool.exe` from the ADK for x64 and ARM64.
- [x] Add hash capture templates under a Foundry-owned WinPE path.
- [x] Add the hash upload OA3 runtime workspace under `X:\Foundry\Runtime\AutopilotHash`.
- [x] Write encrypted Autopilot PFX and PFX password envelopes plus the media secret key through the shared media secret provisioning path.
- [x] Keep current profile JSON staging unchanged in JSON profile mode.
- [x] Do not stage JSON profile folders in hash upload mode unless the user also keeps profiles for another purpose.
- [x] Do not stage `PCPKsp.dll` during media build.
- [x] Add XML documentation comments to new public media asset provisioning APIs and secret envelope APIs.

Automated tests:
- [x] Media asset provisioning writes JSON profile assets only in JSON mode.
- [x] Media asset provisioning writes hash upload assets only in hash mode.
- [x] Missing `oa3tool.exe` produces a clear validation error.
- [x] ADK asset resolution chooses the expected path for x64 and ARM64 media.
- [x] Encrypted Autopilot secrets require a media secret key.
- [x] A media secret key is rejected when no encrypted media secrets exist.
- [x] `WinPE-SecureStartup` is included in the default optional component list and existing DISM optional component failure handling surfaces package failures.
- [x] `dotnet test .\src\Foundry.Core.Tests\Foundry.Core.Tests.csproj --configuration Debug /p:Platform=x64 --no-restore --verbosity minimal` passed: 246 tests, 0 failures.
- [x] `dotnet test .\src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj --configuration Debug /p:Platform=x64 --verbosity minimal` passed: 162 tests, 0 failures.
- [x] `dotnet build .\src\Foundry\Foundry.csproj --configuration Debug /p:Platform=x64 --no-restore --verbosity minimal` passed: 0 warnings, 0 errors.

Manual checks:
Validated from x64 boot.wim extracts under `E:\Test\JSON` and `E:\Test\HASH`. ARM64 media generation remains deferred until ARM64 ADK/media validation is available.

- [x] Build x64 ISO in JSON profile mode and confirm existing profile files are present.
- [x] Build x64 ISO in hash upload mode and confirm OA3/hash assets are present.
- [ ] Build ARM64 ISO in JSON profile mode and confirm existing profile files are present.
- [ ] Build ARM64 ISO in hash upload mode and confirm OA3/hash assets are present.
- [x] Confirm `WinPE-SecureStartup` is present in the mounted image package list.
- [x] Confirm no plaintext PFX, PFX password, private key, token, or client secret is written to Foundry-authored media payloads.

### Phase 5: Foundry Deploy Runtime Branching
PR title: `feat(deploy): branch autopilot runtime by provisioning mode`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Load Autopilot provisioning mode from deploy config.
- [x] Expose mode in startup snapshot, preparation view model, launch request, deployment context, and runtime state.
- [x] Consume the hardware hash group tag choice captured by the Phase 3 Computer Target UX.
- [x] Update `DeploymentLaunchPreparationService` validation:
  - JSON mode requires selected profile.
  - Hash upload mode keeps deployment start available; unusable or expired upload settings are represented as non-blocking runtime skip states.
- [x] Rename or replace `StageAutopilotConfigurationStep` with a mode-aware `ProvisionAutopilotStep`.
- [x] Update `DeploymentStepNames.All`, dependency injection registration, and sequence validation tests together when the Autopilot step is renamed or replaced.
- [x] Keep the Autopilot provisioning step after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- [x] JSON mode copies `AutopilotConfigurationFile.json` from the mode-aware Autopilot provisioning step.
- [x] Hash upload mode skips JSON staging and records planned hash capture/upload status from the same late Autopilot provisioning step.
- [x] Update deployment summary, logs, and telemetry with mode, planned hash-upload status, and retained diagnostics path.
- [x] Add runtime status states for hash upload warnings, skipped states, and later Graph import outcomes without requiring a live Graph call in this phase.
- [x] Persist sanitized Autopilot diagnostics under `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash`.
- [x] Add XML documentation comments to new public deployment runtime contracts and step classes.

Automated tests:
- [x] JSON mode still stages the profile to `Windows\Provisioning\Autopilot`.
- [x] Hash upload mode skips JSON staging.
- [x] Dry run creates a hash-mode manifest without touching Graph.
- [x] Autopilot provisioning step is ordered after `SealRecoveryPartition` and before `FinalizeDeploymentAndWriteLogs`.
- [x] Hash upload mode records diagnostics only after the target Windows root is available. Target Windows `System32` validation remains in Phase 6 with `PCPKsp.dll` copy.
- [x] Launch preparation keeps deployment start available for expired hash upload settings; incomplete settings become runtime skip diagnostics.
- [x] Expired certificate state hides hardware hash group tag controls and leaves deployment start available.
- [x] Runtime state can represent a skipped Autopilot hash upload without failing the deployment state machine.
- [x] Deploy-side configuration deserializes encrypted PFX and PFX password envelopes for later Graph auth phases.
- [x] `dotnet test .\src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj --configuration Debug /p:Platform=x64 --no-restore --verbosity minimal` passed: 165 tests, 0 failures.

Manual checks:
Manual UI/runtime validation remains deferred to an operator run with generated media.

- [ ] Deploy dry-run in JSON mode.
- [ ] Deploy dry-run in hash upload mode.
- [ ] Confirm summary page displays the selected Autopilot method.
- [ ] In hash mode, confirm the group tag selected on Computer Target flows into the runtime launch request.
- [ ] In JSON mode, confirm no hardware hash group tag state is carried into the runtime launch request.
- [ ] In hash mode with expired certificate, confirm Deploy shows the regeneration/recreate media message and still allows OS deployment.
- [ ] Confirm logs contain mode, hash capture diagnostics path, and upload state.

### Phase 6: Hash Capture Service
PR title: `feat(deploy): capture autopilot hardware hash in WinPE`

Implementation progress:
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [x] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Add a C# service that runs OA3Tool with controlled working directory paths.
- [x] Add a C# service that copies `PCPKsp.dll` from `<target Windows>\Windows\System32` to `X:\Windows\System32`.
- [x] Validate source and destination architecture assumptions for x64 and ARM64.
- [x] Generate `OA3.cfg` and dummy input XML internally.
- [x] Validate `OA3.xml` exists.
- [x] Extract serial number and hardware hash.
- [x] Write a local CSV artifact for troubleshooting.
- [x] Preserve OA3 logs in Foundry deployment logs.
- [x] Return structured failure codes for missing tool, `PCPKsp.dll` copy/load failure, empty hash, invalid XML, missing serial, and OA3 exit failure.
- [x] Add XML documentation comments to new public hash capture, OA3Tool, parser, and artifact writer APIs.

Automated tests:
- [x] Resolves the applied Windows `System32` source path.
- [x] Copies `PCPKsp.dll` to `X:\Windows\System32` before OA3Tool execution.
- [x] Parses valid `OA3.xml`.
- [x] Rejects missing `HardwareHash`.
- [x] Rejects invalid XML.
- [x] Generates CSV without quotes, extra columns, or Unicode encoding.
- [x] Sanitizes commas from group tag and serial number.
- [x] `dotnet test .\src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj --configuration Debug /p:Platform=x64 --no-restore --verbosity minimal` passed: 176 tests, 0 failures.
- [x] `dotnet build .\src\Foundry.Deploy\Foundry.Deploy.csproj --configuration Debug /p:Platform=x64 --no-restore --verbosity minimal` passed: 0 warnings, 0 errors.

Manual checks:
Manual runtime validation remains deferred to physical x64 and ARM64 media runs. Graph import validation remains Phase 7.

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
- [x] Phase branch created from `feature/autopilot-hash-upload-foundation`.
- [x] Implementation checklist complete.
- [x] Automated tests complete.
- [x] Manual checks complete or explicitly deferred.
- [ ] PR opened with the planned title.
- [ ] PR merged back into `feature/autopilot-hash-upload-foundation`.

- [x] Add a minimal Graph Autopilot import client.
- [x] Add certificate-based credential creation from decrypted in-memory certificate material.
- [x] Reject any non-certificate authentication mode in WinPE.
- [x] Implement import request.
- [x] Implement polling for import completion.
- [x] Implement polling until the uploaded serial number appears in Windows Autopilot devices.
- [x] Add a 10-minute default timeout for Windows Autopilot device visibility polling.
- [x] Map Graph errors to operator-readable messages.
- [x] Add retry/backoff for transient HTTP failures.
- [x] Treat certificate, tenant, token, consent, permission, Conditional Access, Intune availability, Graph connectivity, `ImportFailed`, duplicate import, and `ImportTimedOut` states as non-blocking Autopilot failures that continue OS deployment.
- [x] Keep destructive cleanup out of the final hash upload workflow.
- [x] Sanitize `AutopilotUploadResult.json` before retaining it in `Windows\Temp\Foundry`.
- [x] Add XML documentation comments to new public Graph client, import polling, and retry-policy APIs.

Automated tests:
- [x] Serializes import payload correctly.
- [x] Sends hardware identifier in the expected Graph format.
- [x] Decrypts PFX material in memory and does not write a decrypted PFX, PFX password, or private key to disk.
- [x] Fails clearly when tenant ID, client ID, certificate thumbprint, or encrypted certificate material is missing.
- [x] Treats certificate, tenant, token, permission, consent, Conditional Access, Intune availability, and Graph connectivity failures as skipped Autopilot, not failed deployment.
- [x] Treats duplicate import errors, `ImportFailed`, and `ImportTimedOut` as Autopilot warnings/failures that do not stop OS deployment.
- [x] Handles `complete`.
- [x] Handles imported identity completion followed by Windows Autopilot device visibility.
- [x] Handles Windows Autopilot device visibility timeout as an automatic warning/non-blocking continuation to the next deployment step.
- [x] Handles `error` with device error code/name.
- [x] Times out with a clear message.
- [x] Retries transient failures only.
- [x] Sanitized upload result omits access tokens, authorization headers, raw request bodies, raw response bodies, PFX bytes, passwords, private key material, and full certificate data.

Manual checks are deferred until a physical WinPE run against a test Intune tenant is available. The automated Phase 7 coverage validates Graph payload shape, retry behavior, import/device polling states, non-blocking deployment continuation, certificate assertion creation, media secret decryption, and sanitized retained result output.

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

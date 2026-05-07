# Business Workflow Phases

## Phase 14: Expert Deploy Configuration Workflow

**Priority:** medium.

**Goal:** port expert mode without mixing it into the standard workflow.

**Prerequisites and boundary:** Phase 11 shell/overlay behavior, Phase 12 WinPE/media services, and Phase 13 media preflight must be complete before Phase 14 implementation starts. Phase 13 item **13.18** intentionally keeps final ISO/USB execution disabled until deploy configuration, Connect provisioning, secret, runtime payload, and optional Autopilot/customization readiness are complete. Phase 14 may make deploy configuration readiness real, but it must not complete **13.18.1** or enable final media execution; that belongs to Phase 16.E final media command enablement.

**Scope boundary:** Phase 14 owns Expert Deploy configuration state, runtime deploy configuration generation, and deployment localization behavior. It may create or bind minimal expert page state needed for persistence and runtime config generation, but full Network provisioning belongs to Phase 15, and full Autopilot/customization workflows belong to Phase 16.

- [x] **14.1** Port expert sections without exposing `General` as an Expert navigation page:
  - [x] Network document state only; full provisioning UI and runtime handoff are Phase 15.
  - [x] Localization.
  - [x] Autopilot document state only; profile import, tenant download, and profile embedding are Phase 16.
  - [x] Customization document state only; deployment customization controls are Phase 16.
  - [x] Keep `General` in the General navigation section.
  - [x] Preserve the existing expert document `general` section internally when required by schema compatibility.
- [x] **14.1.1** Keep expert `Localization` scoped to OS deployment localization, not WinPE boot language:
  - [x] Port WPF `LocalizationSettingsViewModel` behavior for deployment language selection.
  - [x] Preserve `LocalizationSettings.VisibleLanguageCodes`.
  - [x] Preserve `LocalizationSettings.DefaultLanguageCodeOverride`.
  - [x] Preserve `LocalizationSettings.DefaultTimeZoneId`.
  - [x] Preserve `LocalizationSettings.ForceSingleVisibleLanguage`.
  - [x] Preserve `DeployConfigurationGenerator` mapping to `DeployLocalizationSettings` for `Foundry.Deploy`.
  - [x] Do not move `GeneralSettings.WinPeLanguage` into the expert `Localization` page.
- [x] **14.1.2** Preserve deployment timezone runtime support:
  - [x] Add or update the `Foundry.Deploy` runtime model for `localization.defaultTimeZoneId`.
  - [x] Keep `Foundry.Deploy` backward compatible when `defaultTimeZoneId` is missing.
  - [x] Preserve bootstrap timezone resolution order:
    - [x] `FOUNDRY_WINPE_TIMEZONE_ID`.
    - [x] `foundry.deploy.config.json` `localization.defaultTimeZoneId`.
    - [x] Public-IP auto-detection mapped through `iana-windows-timezones.json`.
    - [x] `UTC` fallback.
  - [x] Do not confuse deployment timezone with WinPE boot display language from `General`.
- [x] **14.2** Persist Expert Deploy configuration through the approved app workflow state path.
- [x] **14.3** Do not add user-facing manual configuration file commands.
- [x] **14.4** Generate the `Foundry.Deploy` runtime configuration internally for media creation.
- [x] **14.5** Preserve JSON defaults.
- [x] **14.6** Preserve validation behavior.
- [x] **14.7** Preserve schema compatibility.
- [x] **14.7.1** Generate complete effective `Foundry.Deploy` runtime configuration documents:
  - [x] Always include `schemaVersion`.
  - [x] Always include `localization`.
  - [x] Always include `customization`.
  - [x] Always include `autopilot`.
  - [x] Serialize effective default values instead of relying on missing root sections.
  - [x] Keep `Foundry.Deploy` tolerant for missing optional properties, but do not rely on sparse generated media configs.
  - [x] Add or update `Foundry.Deploy` validation for contradictory effective states.
  - [x] Generated WinUI media always includes a complete effective `foundry.deploy.config.json`, even for standard workflow defaults.
  - [x] Treat this as an intentional WinUI migration improvement over WPF, where deploy config embedding was tied to expert mode.
- [x] **14.7.2** Wire deploy configuration readiness into the `Start` page preflight without enabling final ISO/USB execution:
  - [x] Replace the Phase 13 hardcoded deploy readiness placeholder with real deploy configuration readiness.
  - [x] Keep runtime, Connect provisioning, network provisioning, secret readiness, and final execution gates blocked until their planned phases are complete.
  - [x] Leave **13.18.1** unchecked until Phase 16.E final media command enablement.
- [x] **14.8** Add tests only where business logic changed.
- [x] **14.9** Commit:

```powershell
git commit -m "feat(configuration): port expert deploy configuration workflow"
```

**Validation**

- [x] **14.10** Confirm no user-facing manual configuration file commands are exposed.
- [x] **14.11** Validate Expert Deploy settings persist through the normal app workflow state path.
- [x] **14.12** Validate generated runtime Deploy config for representative standard and expert settings.
- [ ] **14.13** Generate deploy config and validate `Foundry.Deploy` can consume it.
  - Deferred until Phase 16.E enables final ISO/USB media creation; Phase 14 validates generation and model compatibility, but cannot run the real generated-media `Foundry.Deploy` path yet.
- [x] **14.14** Validate `Foundry.Deploy` applies or tolerates `localization.defaultTimeZoneId`.
- [x] **14.15** Validate generated standard media deploy config contains complete default root sections.

## Phase 15: Network Provisioning Workflow

**Priority:** medium.

**Goal:** port network and Wi-Fi provisioning logic used by Foundry.Connect handoff.

**Prerequisites and boundary:** Phase 14 must provide Deploy configuration state, runtime Deploy config generation, and deploy configuration readiness before Phase 15 starts. Phase 15 may make Connect, network, and required-secret readiness real for the `Start` page preflight, but it must still leave final ISO/USB execution disabled until Phase 16.E final media command enablement.

**Recommended implementation split:** Phase 15 has security-sensitive and runtime-sensitive work. Prefer focused PRs under the same phase rather than one broad branch:

- [x] `15.A` owns Network page state, validation, and WPF-compatible network asset selection.
- [x] `15.B` owns complete effective `Foundry.Connect` configuration generation and asset copy layout.
- [x] `15.C` owns embedded secret envelopes, per-media key lifecycle, and `Foundry.Connect` runtime decryption.
- [x] `15.D` owns `Start` page preflight readiness wiring for Connect, network, and required secrets without enabling final media execution.

**Phase 15 delivery workflow:** implement each split in a separate worktree, branch, and pull request. After each split is implemented, run automated verification, complete any required manual validation, wait for CI to pass, squash-merge the PR, sync `feat/winui-migration`, clean the worktree, then start the next split. If `15.C` grows too large, split it again into secret envelope generation and `Foundry.Connect` runtime decryption PRs.

- [x] **15.1** Port network settings model bindings.
- [x] **15.2** Port Wi-Fi settings validation.
- [x] **15.3** Port 802.1X settings.
- [x] **15.4** Port certificate picker through WinUI shell service.
- [x] **15.5** Port provisioning bundle creation.
- [x] **15.6** Preserve `Foundry.Connect` configuration compatibility.
- [x] **15.6.1** Generate complete effective `Foundry.Connect` runtime configuration documents:
  - [x] Always include `schemaVersion`.
  - [x] Always include `capabilities`.
  - [x] Always include `dot1x`.
  - [x] Always include `wifi`.
  - [x] Always include `internetProbe`.
  - [x] Serialize effective default values instead of relying on missing root sections.
  - [x] Keep `Foundry.Connect` tolerant for missing optional properties, but do not rely on sparse generated media configs.
  - [x] Add or update `Foundry.Connect` validation for contradictory effective states.
- [x] **15.6.1.1** Replace any generated-media fallback that emits sparse or legacy-shaped Connect JSON with the same complete effective generator used by the Network workflow.
- [x] **15.6.2** Define the embedded secret schema explicitly:
  - [x] Use a new generated config property for encrypted values, for example `passphraseSecret`.
  - [x] Do not write personal Wi-Fi passphrases to generated media as plaintext `passphrase` values.
  - [x] Keep `Foundry.Connect` tolerant for legacy plaintext `passphrase` when running old or standalone configs.
  - [x] Do not persist Wi-Fi or network plaintext secrets in `foundry.expert.config.json`; keep authoring-time secret values transient until generated media encryption is performed.
  - [x] Use this envelope shape for encrypted generated media secrets:

```json
{
  "kind": "encrypted",
  "algorithm": "aes-gcm-v1",
  "keyId": "media",
  "nonce": "<base64url>",
  "tag": "<base64url>",
  "ciphertext": "<base64url>"
}
```

  - [x] No generated Connect config schema bump is required for the additive `passphraseSecret` model; legacy plaintext remains tolerated.
  - [x] Document normalization rules so WPF JSON comparisons ignore the intentional plaintext-to-envelope change.
- [x] **15.7** Preserve asset file preparation behavior.
  - [x] **15.7.1** Use explicit WinUI `PasswordBox` handling for Wi-Fi and network secrets.
  - [x] **15.7.2** Never log network secrets.
  - [x] **15.7.3** Never display network secrets in the Start summary.
  - [x] **15.7.4** Serialize secrets only when required by the runtime or configuration contract.
  - [x] **15.7.5** Do not serialize Wi-Fi or network secrets as plaintext when embedded for unattended WinPE execution.
  - [x] **15.7.6** Do not use DPAPI for generated WinPE runtime secrets because WinPE cannot decrypt data tied to the authoring Windows user or machine context.
  - [x] **15.7.7** Add an explicit secret envelope for embedded runtime secrets:
    - [x] Use `aes-gcm-v1`.
    - [x] Use `System.Security.Cryptography.AesGcm`.
    - [x] Generate a random 256-bit per-media key with `RandomNumberGenerator`.
    - [x] Generate a random 96-bit nonce per encrypted value.
    - [x] Use a 128-bit authentication tag.
    - [x] Use the .NET `AesGcm` constructor overload that declares the tag size.
    - [x] Store base64url-encoded nonce, tag, and ciphertext in the configuration secret envelope.
    - [x] Store the per-media key separately under `X:\Foundry\Config\Secrets\media-secrets.key`.
    - [x] Generate the per-media key during media provisioning bundle creation, not at app startup or settings save time.
    - [x] Copy the per-media key into the generated ISO/USB boot image only when encrypted secrets are present.
    - [x] No transient host-side secret-key staging file is written by Phase 15.C.
    - [x] Resolve the per-media key automatically in `Foundry.Connect`; never ask the operator for a decryption key.
    - [x] Never store the per-media key inline in `foundry.connect.config.json`.
    - [x] Never log the key, plaintext secret, ciphertext, tag, or nonce.
  - [x] **15.7.8** Document the security boundary:
    - [x] Embedded encrypted secrets prevent casual JSON inspection.
    - [x] Embedded encrypted secrets are not a strong boundary against an attacker who has the boot media and key file.
  - [x] **15.7.9** Preserve the WPF-compatible network asset layout:
    - [x] Wired profiles: `Network\Wired\Profiles`.
    - [x] Wi-Fi profiles: `Network\Wifi\Profiles`.
    - [x] Wired certificates: `Network\Certificates\Wired`.
    - [x] Wi-Fi certificates: `Network\Certificates\Wifi`.
    - [x] Generated config-relative paths must match these folders.
- [x] **15.7.10** Wire Connect, network, and required-secret readiness into the `Start` page preflight without enabling final ISO/USB execution:
  - [x] Replace the Phase 13 hardcoded network, Connect provisioning, and required-secret readiness placeholders with real readiness values.
  - [x] Keep runtime payload readiness and final execution gates blocked until Phase 16.E final media command enablement.
- [x] **15.8** Commit:

```powershell
git commit -m "feat(network): wire connect preflight readiness"
```

**Validation**

- [x] **15.9** Existing Connect provisioning and runtime configuration tests pass.
- [x] **15.10** Generated `Foundry.Connect` configuration matches the preserved WPF-compatible asset layout for equivalent settings.
- [x] **15.11** Certificate asset copy behavior is preserved.
  - [x] Wired certificates are copied to `Network\Certificates\Wired`.
  - [x] Wi-Fi certificates are copied to `Network\Certificates\Wifi`.
  - [x] Config-relative certificate paths point to those folders.
- [x] **15.12** Generated `foundry.connect.config.json` has every schema root section present.
- [x] **15.13** Embedded Wi-Fi/network secrets are represented as secret envelopes, not plaintext strings.
- [x] **15.14** `Foundry.Connect` can decrypt embedded `aes-gcm-v1` secrets in runtime tests.
  - [ ] Real generated boot-image validation is deferred until ISO/USB media creation is enabled.
- [x] **15.15** `Foundry.Connect` decrypts embedded secrets automatically without prompting the operator for a decryption key.
  - [ ] Real generated boot-image validation is deferred until ISO/USB media creation is enabled.
- [x] **15.16** Logs, validation errors, summaries, and UI diagnostics redact:
  - [x] Plaintext secrets.
  - [x] Per-media keys.
  - [x] Ciphertext.
  - [x] Nonces.
  - [x] Tags.
- [x] **15.17** Generated media contains `media-secrets.key` only when an encrypted secret envelope is present.
  - [x] Validate this at the service/workspace provisioning level during Phase 15; full generated ISO/USB execution remains deferred until Phase 16.E final media command enablement.
- [x] **15.18** WPF comparison tests account for intentional WinUI changes:
  - [x] Complete effective Connect config documents.
  - [x] Complete effective Deploy config documents.
  - [x] Encrypted secret envelopes instead of plaintext generated media secrets.
- [x] **15.19** `Start` page preflight shows real network, Connect, and required-secret readiness while final ISO/USB execution remains deferred.

## Phase 16: Autopilot And Customization Workflows

**Priority:** medium-low.

**Goal:** port remaining expert workflow features after core media creation is functional.

**Prerequisites and boundary:** Phase 16 extends the media workflow with optional Autopilot and customization payloads. Media creation must remain usable when Autopilot is disabled. Microsoft Graph authentication, token cache behavior, environment-variable-driven Graph configuration, and Graph HTTP calls must remain in the WinUI `Foundry` app/infrastructure layer, not in `Foundry.Core`.

**Scope boundary:** preserve user-visible WPF parity for machine naming, Autopilot profile import, Autopilot tenant download, profile selection, generated deploy config, and WinPE profile embedding where those contracts remain valid. Do not preserve obsolete WPF-era runtime quirks. Do not implement APPX removal or custom deploy configuration editing in Phase 16; the WPF app only exposed those as disabled placeholders.

**Architecture direction:** do not add backward-compatibility logic for obsolete WPF-era contracts. If Phase 16 reveals that `Foundry.Deploy`, `Foundry.Connect`, or shared configuration contracts should be simplified or adjusted, make the focused change in the same split and update the generated runtime contracts/tests accordingly. Prefer clear WinUI/Core-era contracts and maintainable runtime behavior over preserving legacy implementation quirks.

**Recommended implementation split:** Phase 16 has a low-risk local customization slice and higher-risk Autopilot/Graph slices. Implement each split in a separate worktree, branch, and pull request. After each split is implemented, run automated verification, complete required manual validation, wait for CI to pass, squash-merge the PR, sync `feat/winui-migration`, clean the worktree, then start the next split.

- [x] **16.A** Port customization machine naming.
  - [x] **16.A.1** Add the WinUI customization settings page state for:
    - [x] Enable machine naming rules.
    - [x] Machine name prefix.
    - [x] Auto-generate the suffix after the prefix.
    - [x] Allow manual editing after the prefix.
  - [x] **16.A.2** Preserve WPF normalization:
    - [x] Disabled machine naming writes no prefix.
    - [x] Disabled machine naming forces `autoGenerateName=false`.
    - [x] Disabled machine naming forces `allowManualSuffixEdit=true`.
    - [x] Enabled machine naming trims the prefix before persistence/generation.
  - [x] **16.A.2.1** Validate machine naming prefix early in the WinUI authoring flow using the same computer-name rules that `Foundry.Deploy` enforces at runtime.
  - [x] **16.A.3** Preserve generated Deploy config output for `customization.machineNaming`.
  - [x] **16.A.4** Keep APPX removal and custom deploy configuration editing out of scope.

- [x] **16.B** Port Autopilot manual profile import and local profile management.
  - [x] **16.B.1** Add the WinUI Autopilot page state for:
    - [x] Enable Autopilot.
    - [x] Import profile JSON.
    - [x] Remove selected profile.
    - [x] Select default profile.
    - [x] Show imported profiles with display name, source, imported timestamp, and folder name.
  - [x] **16.B.2** Preserve WPF manual import behavior:
    - [x] Parse and validate JSON.
    - [x] Reject empty, invalid, or non-ASCII profile JSON.
    - [x] Resolve display name from `Comment_File`, filename, or parent folder.
    - [x] Generate `manual-<sha>` IDs from profile JSON.
    - [x] Sanitize profile folder names.
    - [x] Merge duplicate profiles by ID.
    - [x] Sort profiles by display name, then ID.
    - [x] Fall back to the first profile when the default profile is removed or missing.
  - [x] **16.B.3** Persist full imported profile JSON in expert configuration.
  - [x] **16.B.3.1** Add explicit expert configuration state updates for Autopilot and Customization:
    - [x] `UpdateAutopilot(AutopilotSettings settings)`.
    - [x] `UpdateCustomization(CustomizationSettings settings)`.
  - [x] **16.B.4** Preserve generated Deploy config output:
    - [x] `autopilot.isEnabled`.
    - [x] `autopilot.defaultProfileFolderName`.
  - [x] **16.B.5** Preserve Autopilot profile path contracts:
    - [x] Embedded WinPE relative path: `Foundry\Config\Autopilot\<FolderName>\AutopilotConfigurationFile.json`.
    - [x] Runtime WinPE path consumed by `Foundry.Deploy`: `X:\Foundry\Config\Autopilot\<FolderName>\AutopilotConfigurationFile.json`.
    - [x] Target Windows staging path written by `Foundry.Deploy`: `%SystemDrive%\Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json`.
    - [x] Generated Deploy config stores the selected/default profile folder name, not a full path.

- [x] **16.C** Port Autopilot Microsoft Graph tenant import.
  - [x] **16.C.1** Keep Microsoft Graph authentication in the WinUI `Foundry` app/infrastructure layer.
  - [x] **16.C.2** Keep `InteractiveBrowserCredential`, token cache behavior, environment-variable-driven Graph configuration, and Graph HTTP calls out of `Foundry.Core`.
  - [x] **16.C.3** Preserve WPF Graph behavior:
    - [x] Use `DeviceManagementServiceConfig.Read.All` and `User.Read` scopes.
    - [x] Preserve `FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID` and `FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID` overrides.
    - [x] Query `v1.0/organization?$select=id,verifiedDomains` for tenant information and verified domain.
    - [x] Query `beta/deviceManagement/windowsAutopilotDeploymentProfiles`.
    - [x] Handle paged Graph responses.
    - [x] Convert tenant deployment profiles into offline `AutopilotConfigurationFile.json` content.
  - [x] **16.C.4** Use a cancellable WinUI `ContentDialog` for tenant authentication/download, then close it before showing profile selection.
  - [x] **16.C.5** Use a WinUI `ContentDialog` for downloaded profile selection instead of a separate WPF-style window:
    - [x] Profiles selected by default.
    - [x] Select all.
    - [x] Clear.
    - [x] Selected count.
    - [x] Import disabled when no profile is selected.
    - [x] Cancel returns no imported profiles.
  - [x] **16.C.6** Do not show tenant tokens or raw Graph response bodies in UI, summaries, logs, or diagnostics.

- [x] **16.D** Wire Autopilot/customization readiness into `Start` preflight and finish Phase 16 validation.
  - [x] **16.D.1** Do not block media creation when Autopilot is disabled.
  - [x] **16.D.2** Block or warn when Autopilot is enabled but no valid default profile is selected.
  - [x] **16.D.3** Show selected Autopilot profile metadata without exposing tenant tokens or Graph response bodies.
  - [x] **16.D.4** Keep final ISO/USB execution disabled until Phase 16.E final media command enablement.
  - [x] **16.D.5** Preserve generated Deploy config and Autopilot profile path contracts across the final Phase 16 state.
  - [x] **16.D.6** Update `Foundry.Deploy` Autopilot startup behavior so generated Deploy config is the source of truth:
    - [x] Respect `autopilot.isEnabled=false` even when embedded Autopilot profiles exist.
    - [x] Select/use a default Autopilot profile only when `autopilot.isEnabled=true`.
    - [x] Remove any automatic enablement based only on profile presence.

- [x] **16.6** Commit each split independently:

```powershell
git commit -m "feat(customization): port machine naming settings"
git commit -m "feat(autopilot): port manual profile import"
git commit -m "feat(autopilot): add graph profile import"
git commit -m "feat(autopilot): wire start readiness"
```

**Validation**

- [x] **16.7** Existing Autopilot-related and Deploy configuration tests pass.
- [x] **16.8** Generated Deploy config includes machine naming and selected Autopilot profile settings.
- [x] **16.9** WinPE asset provisioning service-level validation includes expected Autopilot profile payloads; real generated boot-media validation remains deferred until final ISO/USB media execution is enabled.
- [x] **16.10** `Foundry.Core` has no dependency on Azure Identity, Microsoft Graph clients, `InteractiveBrowserCredential`, or Graph HTTP plumbing.
- [x] **16.11** Manual Autopilot JSON import rejects empty, invalid, or non-ASCII JSON.
- [x] **16.12** Manual Autopilot JSON import preserves WPF-compatible profile ID, display name, folder name, merge, sort, and default-profile fallback behavior.
- [x] **16.13** Graph tenant import downloads profiles through a cancellable WinUI `ContentDialog` and imports selected profiles through a separate WinUI `ContentDialog`.
- [x] **16.14** Autopilot disabled does not block `Start` preflight.
- [x] **16.15** Autopilot enabled without a valid default profile blocks or warns in `Start` preflight.
- [ ] **16.16** Deferred until final media generation is enabled: boot generated media into `Foundry.Deploy` with Autopilot enabled but no embedded profile, and confirm the Autopilot section remains visible so the operator can disable it.

## Phase 16.E: Final Media Command Enablement

**Priority:** high before compatibility testing.

**Goal:** turn the `Start` page from readiness-only mode into the real ISO/USB execution entry point after Deploy, Connect, network, secret, runtime payload, and optional Autopilot/customization readiness are all real.

**Prerequisites and boundary:** Phases 14, 15, and 16 must be complete. Phase 16.E owns final media command enablement and must absorb deferred real-media validations from Phases 12, 13, 14, 15, and 16. Phase 16.E should not perform the broader Connect/Deploy compatibility pass; that remains Phase 17 after media can be generated from WinUI.

- [x] **16.E.1** Replace remaining final media placeholders in `Start` preflight:
  - [x] Runtime payload readiness is real, not hardcoded.
  - [x] `Foundry.Connect` runtime payload availability is checked for the selected RID.
  - [x] `Foundry.Deploy` runtime payload availability is checked for the selected RID.
  - [x] Blocking reasons identify missing runtime payloads separately from Deploy, Connect, network, secret, or Autopilot readiness.
- [x] **16.E.2** Wire final ISO/USB command enablement:
  - [x] Enable `Create ISO` only when ADK, ISO path, architecture, Deploy, Connect, network, required secrets, runtime payloads, and optional Autopilot readiness pass.
  - [x] Enable `Create USB` only when all ISO requirements pass and a stable USB target is selected.
  - [x] Keep USB creation disabled when no USB target is selected without blocking ISO creation.
  - [x] Keep command state readable in the global summary.
- [x] **16.E.3** Wire `Create ISO` execution from WinUI:
  - [x] Use the existing operation overlay contract for progress, completion, and failure; the current overlay has no in-operation cancel affordance.
  - [x] Call the core ISO media service from an app-level orchestration service or view model command, not from page code-behind.
  - [x] Generate the complete effective Deploy and Connect configuration files during media provisioning.
  - [x] Provision runtime payloads, network assets, encrypted secret keys, Autopilot profiles, customization settings, drivers, and boot-language assets into the generated media layout.
- [x] **16.E.4** Wire `Create USB` execution from WinUI:
  - [x] Require an explicit destructive USB confirmation dialog.
  - [x] Default the destructive confirmation to cancel.
  - [x] Revalidate selected USB disk identity immediately before formatting.
  - [x] Use the existing operation overlay contract for progress, completion, and failure; cancellation is limited to the pre-format confirmation step until the operation overlay supports cancellation.
- [x] **16.E.5** Keep secrets safe during final execution:
  - [x] Never log plaintext Wi-Fi/network secrets.
  - [x] Never log media secret keys.
  - [x] Never show plaintext secrets or media keys in summaries, validation errors, or operation output.
  - [x] Create `media-secrets.key` only when generated media contains encrypted secret envelopes.
- [ ] **16.E.6** Commit:

```powershell
git commit -m "feat(media): enable final iso and usb commands"
```

**Validation**

- [ ] **16.E.7** Complete deferred Phase 12 generated media validation:
  - [ ] **12.20** ISO creation works on a test machine with ADK through the final `Start` page command.
  - [ ] **12.21** USB creation works on a disposable test drive through the final `Start` page command when hardware is available.
  - [ ] **12.22** Generated ISO/USB media matches the documented layout from the final `Start` page workflow.
- [ ] **16.E.8** Complete deferred Phase 13 command gate validation:
  - [ ] **13.18.1** ISO/USB creation is blocked when required Connect configuration is incomplete.
  - [ ] **13.18.1** ISO/USB creation is blocked when required Deploy configuration is incomplete.
  - [ ] **13.18.1** ISO/USB creation is blocked when encrypted secret-key provisioning is required but unavailable.
  - [ ] **13.18.1** ISO/USB creation is blocked when runtime payloads are unavailable.
  - [ ] **13.18.1** ISO/USB creation is blocked or warned when Autopilot is enabled without a valid embedded profile.
- [ ] **16.E.9** Complete deferred Phase 14 generated-media validation:
  - [ ] **14.13** Generated media contains `foundry.deploy.config.json`.
  - [ ] **14.13** `Foundry.Deploy` can load the generated Deploy config from generated media.
- [ ] **16.E.10** Complete deferred Phase 15 generated-media validation:
  - [ ] **15.14** `Foundry.Connect` can decrypt embedded `aes-gcm-v1` secrets from generated boot media.
  - [ ] **15.15** `Foundry.Connect` decrypts embedded secrets from generated boot media without prompting the operator for a decryption key.
- [ ] **16.E.11** Complete deferred Phase 16 generated-media validation:
  - [ ] **16.16** Boot generated media into `Foundry.Deploy` with Autopilot enabled but no embedded profile, and confirm the Autopilot section remains visible so the operator can disable it.
  - [ ] Generated media embeds selected Autopilot profiles under `Foundry\Config\Autopilot\<FolderName>\AutopilotConfigurationFile.json`.
  - [ ] Generated Deploy config stores the selected/default Autopilot profile folder name, not a full path.
- [ ] **16.E.12** Logs are readable in `C:\ProgramData\Foundry\Logs\Foundry.log` and include final ISO/USB start, progress, completion, pre-format cancellation, and failure events without exposing secrets.

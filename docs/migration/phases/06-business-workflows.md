# Business Workflow Phases

## Phase 14: Expert Deploy Configuration Workflow

**Priority:** medium.

**Goal:** port expert mode without mixing it into the standard workflow.

**Prerequisites and boundary:** Phase 11 shell/overlay behavior, Phase 12 WinPE/media services, and Phase 13 media preflight must be complete before Phase 14 implementation starts. Phase 13 item **13.18** intentionally keeps final ISO/USB execution disabled until deploy configuration, Connect provisioning, and secret readiness are complete. Phase 14 may make deploy configuration readiness real, but it must not complete **13.18.1** or enable final media execution; that belongs to the final media enablement PR after Phases 14 and 15.

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
  - [x] Leave **13.18.1** unchecked until the final media enablement PR after Phases 14 and 15.
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
  - Deferred until final ISO/USB media creation is enabled after Phase 15; Phase 14 validates generation and model compatibility, but cannot run the real generated-media `Foundry.Deploy` path yet.
- [x] **14.14** Validate `Foundry.Deploy` applies or tolerates `localization.defaultTimeZoneId`.
- [x] **14.15** Validate generated standard media deploy config contains complete default root sections.

## Phase 15: Network Provisioning Workflow

**Priority:** medium.

**Goal:** port network and Wi-Fi provisioning logic used by Foundry.Connect handoff.

**Prerequisites and boundary:** Phase 14 must provide Deploy configuration state, runtime Deploy config generation, and deploy configuration readiness before Phase 15 starts. Phase 15 may make Connect, network, and required-secret readiness real for the `Start` page preflight, but it must still leave final ISO/USB execution disabled until the final media enablement PR.

**Recommended implementation split:** Phase 15 has security-sensitive and runtime-sensitive work. Prefer focused PRs under the same phase rather than one broad branch:

- [ ] `15.A` owns Network page state, validation, and WPF-compatible network asset selection.
- [ ] `15.B` owns complete effective `Foundry.Connect` configuration generation and asset copy layout.
- [ ] `15.C` owns embedded secret envelopes, per-media key lifecycle, and `Foundry.Connect` runtime decryption.
- [ ] `15.D` owns `Start` page preflight readiness wiring for Connect, network, and required secrets without enabling final media execution.

- [ ] **15.1** Port network settings model bindings.
- [ ] **15.2** Port Wi-Fi settings validation.
- [ ] **15.3** Port 802.1X settings.
- [ ] **15.4** Port certificate picker through WinUI shell service.
- [ ] **15.5** Port provisioning bundle creation.
- [ ] **15.6** Preserve `Foundry.Connect` configuration schema.
- [ ] **15.6.1** Generate complete effective `Foundry.Connect` runtime configuration documents:
  - [ ] Always include `schemaVersion`.
  - [ ] Always include `capabilities`.
  - [ ] Always include `dot1x`.
  - [ ] Always include `wifi`.
  - [ ] Always include `internetProbe`.
  - [ ] Serialize effective default values instead of relying on missing root sections.
  - [ ] Keep `Foundry.Connect` tolerant for missing optional properties, but do not rely on sparse generated media configs.
  - [ ] Add or update `Foundry.Connect` validation for contradictory effective states.
- [ ] **15.6.1.1** Replace any generated-media fallback that emits sparse or legacy-shaped Connect JSON with the same complete effective generator used by the Network workflow.
- [ ] **15.6.2** Define the embedded secret schema explicitly:
  - [ ] Use a new generated config property for encrypted values, for example `passphraseSecret`.
  - [ ] Do not write personal Wi-Fi passphrases to generated media as plaintext `passphrase` values.
  - [ ] Keep `Foundry.Connect` tolerant for legacy plaintext `passphrase` when running old or standalone configs.
  - [ ] Use this envelope shape for encrypted generated media secrets:

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

  - [ ] Bump the generated Connect config schema version if the model requires it.
  - [ ] Document normalization rules so WPF JSON comparisons ignore the intentional plaintext-to-envelope change.
- [ ] **15.7** Preserve asset file preparation behavior.
  - [ ] **15.7.1** Use explicit WinUI `PasswordBox` handling for Wi-Fi and network secrets.
  - [ ] **15.7.2** Never log network secrets.
  - [ ] **15.7.3** Never display network secrets in the Start summary.
  - [ ] **15.7.4** Serialize secrets only when required by the runtime or configuration contract.
  - [ ] **15.7.5** Do not serialize Wi-Fi or network secrets as plaintext when embedded for unattended WinPE execution.
  - [ ] **15.7.6** Do not use DPAPI for generated WinPE runtime secrets because WinPE cannot decrypt data tied to the authoring Windows user or machine context.
  - [ ] **15.7.7** Add an explicit secret envelope for embedded runtime secrets:
    - [ ] Use `aes-gcm-v1`.
    - [ ] Use `System.Security.Cryptography.AesGcm`.
    - [ ] Generate a random 256-bit per-media key with `RandomNumberGenerator`.
    - [ ] Generate a random 96-bit nonce per encrypted value.
    - [ ] Use a 128-bit authentication tag.
    - [ ] Use the .NET `AesGcm` constructor overload that declares the tag size.
    - [ ] Store base64url-encoded nonce, tag, and ciphertext in the configuration secret envelope.
    - [ ] Store the per-media key separately under `X:\Foundry\Config\Secrets\media-secrets.key`.
    - [ ] Generate the per-media key during ISO/USB media creation, not at app startup or settings save time.
    - [ ] Copy the per-media key into the generated ISO/USB boot image only when encrypted secrets are present.
    - [ ] Remove any transient host-side secret-key staging file after media creation succeeds or fails.
    - [ ] Resolve the per-media key automatically in `Foundry.Connect`; never ask the operator for a decryption key.
    - [ ] Never store the per-media key inline in `foundry.connect.config.json`.
    - [ ] Never log the key, plaintext secret, ciphertext, tag, or nonce.
  - [ ] **15.7.8** Document the security boundary:
    - [ ] Embedded encrypted secrets prevent casual JSON inspection.
    - [ ] Embedded encrypted secrets are not a strong boundary against an attacker who has the boot media and key file.
  - [ ] **15.7.9** Preserve the WPF-compatible network asset layout:
    - [ ] Wired profiles: `Network\Wired\Profiles`.
    - [ ] Wi-Fi profiles: `Network\Wifi\Profiles`.
    - [ ] Wired certificates: `Network\Certificates\Wired`.
    - [ ] Wi-Fi certificates: `Network\Certificates\Wifi`.
    - [ ] Generated config-relative paths must match these folders.
- [ ] **15.7.10** Wire Connect, network, and required-secret readiness into the `Start` page preflight without enabling final ISO/USB execution:
  - [ ] Replace the Phase 13 hardcoded network, Connect provisioning, and required-secret readiness placeholders with real readiness values.
  - [ ] Keep runtime payload readiness and final execution gates blocked until the final media enablement PR.
- [ ] **15.8** Commit:

```powershell
git commit -m "feat(network): port connect provisioning workflow"
```

**Validation**

- [ ] **15.9** Existing `FoundryConnectProvisioningServiceTests` pass.
- [ ] **15.10** Generated `Foundry.Connect` configuration matches WPF reference for equivalent settings.
- [ ] **15.11** Certificate asset copy behavior is preserved.
  - [ ] Wired certificates are copied to `Network\Certificates\Wired`.
  - [ ] Wi-Fi certificates are copied to `Network\Certificates\Wifi`.
  - [ ] Config-relative certificate paths point to those folders.
- [ ] **15.12** Generated `foundry.connect.config.json` has every schema root section present.
- [ ] **15.13** Embedded Wi-Fi/network secrets are represented as secret envelopes, not plaintext strings.
- [ ] **15.14** `Foundry.Connect` can decrypt embedded `aes-gcm-v1` secrets in WinPE/runtime tests.
- [ ] **15.15** `Foundry.Connect` decrypts embedded secrets automatically without prompting the operator for a decryption key.
- [ ] **15.16** Logs, validation errors, summaries, and UI diagnostics redact:
  - [ ] Plaintext secrets.
  - [ ] Per-media keys.
  - [ ] Ciphertext.
  - [ ] Nonces.
  - [ ] Tags.
- [ ] **15.17** Generated media contains `media-secrets.key` only when an encrypted secret envelope is present.
- [ ] **15.18** WPF comparison tests account for intentional WinUI changes:
  - [ ] Complete effective Connect config documents.
  - [ ] Complete effective Deploy config documents.
  - [ ] Encrypted secret envelopes instead of plaintext generated media secrets.
- [ ] **15.19** `Start` page preflight shows real network, Connect, and required-secret readiness while final ISO/USB execution remains deferred.

## Phase 16: Autopilot And Customization Workflows

**Priority:** medium-low.

**Goal:** port remaining expert workflow features after core media creation is functional.

**Prerequisites and boundary:** Phase 16 extends the media workflow with optional Autopilot and customization payloads. Media creation must remain usable when Autopilot is disabled, and Graph authentication, token cache behavior, environment-variable-driven Graph configuration, and Graph HTTP calls must remain in the WinUI app/infrastructure layer.

- [ ] **16.1** Port Autopilot profile selection.
- [ ] **16.2** Port Autopilot profile import/selection dialog.
  - [ ] **16.2.1** Use the blocking operation overlay for profile import.
  - [ ] **16.2.2** Keep Microsoft Graph authentication in the WinUI `Foundry` app/infrastructure layer.
  - [ ] **16.2.3** Keep `InteractiveBrowserCredential`, token cache behavior, environment-variable-driven Graph configuration, and Graph HTTP calls out of `Foundry.Core`.
  - [ ] **16.2.4** Move only pure Autopilot conversion, validation, and file transformation logic to `Foundry.Core` when useful.
- [ ] **16.3** Port customization settings.
- [ ] **16.3.1** Use the blocking operation overlay for Autopilot tenant download.
- [ ] **16.4** Preserve generated deploy configuration output.
- [ ] **16.5** Preserve profile file embedding into WinPE media.
- [ ] **16.5.1** Wire Autopilot/customization readiness into the `Start` page preflight:
  - [ ] Do not block media creation when Autopilot is disabled.
  - [ ] Block or warn when Autopilot is enabled but no valid profile is selected.
  - [ ] Show selected profile metadata without exposing tenant tokens or Graph response bodies.
- [ ] **16.6** Commit:

```powershell
git commit -m "feat(autopilot): port autopilot and customization workflows"
```

**Validation**

- [ ] **16.7** Existing Autopilot-related tests pass.
- [ ] **16.8** Generated deploy config includes selected profiles.
- [ ] **16.9** WinPE media includes expected profile payload.
- [ ] **16.10** `Foundry.Core` has no dependency on Azure Identity, Microsoft Graph clients, `InteractiveBrowserCredential`, or Graph HTTP plumbing.

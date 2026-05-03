# Resolved Decisions

These decisions are locked for the current migration plan. Reopen one only if implementation proves the decision materially wrong.

- [x] **RD-001: General page ownership**
  - `General` is part of the General navigation section.
  - `General` is not an Expert navigation page.
  - The expert JSON document may still contain a `general` section when required by the existing schema.

- [x] **RD-002: Elevation model**
  - The WinUI Foundry app keeps an application-level administrator requirement during this migration.
  - Use a WinUI app manifest equivalent to the current WPF `requireAdministrator` behavior.
  - Do not introduce per-operation elevation in this migration.

- [x] **RD-003: Blocking operation overlay**
  - Use the same shell-level blocking overlay pattern for global operations.
  - Covered operations are ADK install, ADK upgrade, ISO creation, USB creation, Autopilot profile import, and Autopilot tenant download.
  - Navigation remains blocked until the operation fully completes.

- [x] **RD-004: Startup readiness sequence**
  - Initialize logging, dependency injection, and Velopack startup hooks first.
  - Detect ADK and WinPE Add-on readiness before enabling configuration pages.
  - Apply the shell navigation state after ADK detection.
  - Refresh USB targets only after ADK is compatible.
  - Run update checks after readiness initialization and never block app usage for a startup update check.

- [x] **RD-005: Autopilot Graph ownership**
  - Microsoft Graph authentication remains in the WinUI `Foundry` app/infrastructure layer.
  - `Foundry.Core` may contain pure conversion, validation, and file transformation logic.
  - `Foundry.Core` must not own `InteractiveBrowserCredential`, Graph HTTP calls, token cache behavior, or environment-variable-driven Graph configuration.

- [x] **RD-006: Network secret handling**
  - WinUI uses explicit `PasswordBox` handling for Wi-Fi and network secrets.
  - Secrets may live in memory as today while needed for workflow execution.
  - Secrets must not be logged.
  - Secrets must not be displayed in summary pages.
  - Secrets are serialized only when required by the runtime or configuration contract.

- [x] **RD-007: Release notes rendering**
  - Do not port the WPF `FlowDocument` release-notes renderer 1:1.
  - Use Velopack release notes as the source for update details.
  - Render release notes with a simple WinUI-friendly view or simplified text.

- [x] **RD-008: App settings persistence**
  - Replace `nucs.JsonSettings` with an internal `IAppSettingsService`.
  - Keep the service UI-agnostic and testable.
  - Persist app-level settings such as theme, app language, update preference/feed/channel, and diagnostics/log preferences when needed.

- [x] **RD-009: Supported Foundry architectures**
  - The WinUI `Foundry` app supports `win-x64` and `win-arm64`.
  - Remove `x86` and `win-x86` from the imported WinUI prototype.
  - Do not build, test, package, or release x86 Foundry artifacts.

- [x] **RD-010: DevWinUI shell baseline**
  - DevWinUI remains the long-term Foundry shell baseline after the migration.
  - Keep DevWinUI navigation, title bar, breadcrumb, settings, and content-frame conventions unless a specific package becomes unused or blocks implementation.
  - Remove prototype metadata and placeholder strings, not the DevWinUI shell approach.

- [x] **RD-011: Date-based release versioning**
  - Keep date-based release versioning.
  - Do not switch Foundry to conventional product SemVer such as `1.2.3`.
  - Keep Git tags and GitHub releases in the existing visible format `vYY.M.D.Build`, for example `v26.4.30.1`.
  - Keep the app display version and assembly/file versions as `YY.M.D.Build`, for example `26.4.30.1`.
  - Convert the date-based version to a Velopack-compatible SemVer2 package version with `YY.M.D-build.Build`, for example `26.4.30-build.1`.
  - The mapping is deterministic:
    - `v26.4.30.1` maps to `26.4.30-build.1`.
    - `v26.4.30.2` maps to `26.4.30-build.2`.
    - `v26.5.1.1` maps to `26.5.1-build.1`.

- [x] **RD-012: WinUI Foundry localization format**
  - The migrated WinUI `Foundry` app uses `.resw` resources for all app UI and view-model-facing localized text.
  - Do not keep `.resx` localization in the migrated WinUI `Foundry` app.
  - `Foundry.Core` exposes stable codes, values, or invariant diagnostics where possible; WinUI `Foundry` owns user-facing localization through `.resw`.
  - `Foundry.Connect` and `Foundry.Deploy` may keep their existing WPF `.resx` localization because they are not migrated to WinUI.
  - DevWinUI navigation metadata must follow the DevWinUI `AppData.json` localization contract: use `LocalizeId` with `UsexUid=true` for NavigationView groups/items when supported.
  - Runtime language switching must not require restarting Foundry. Already-loaded WinUI and DevWinUI surfaces must be refreshed, rebound, or rebuilt after changing `ApplicationLanguages.PrimaryLanguageOverride`.
  - Use the old `E:\Github\Bucket_v2_old` DevWinUI localization pattern as implementation guidance: initialize language before window creation, use `ResourceManager` plus a language `ResourceContext` for runtime string lookup, persist validated language, and reinitialize DevWinUI JSON navigation after a runtime language change.

- [x] **RD-013: WPF reference archive lifetime**
  - Keep `archive\Foundry.WpfReference` during the migration as a read-only implementation reference.
  - Remove the archive after final WinUI cutover and the first stable WinUI release validation.
  - Do not keep the WPF reference archive permanently in the repository.

- [x] **RD-014: ProgramData-only app data**
  - The WinUI `Foundry` app writes app data, settings, logs, cache, temp files, and authoring workspaces under `C:\ProgramData\Foundry`.
  - Do not use DevWinUI's prototype AppData root as Foundry's runtime data root.
  - Store app settings at `C:\ProgramData\Foundry\Settings\appsettings.json`.
  - Store logs at `C:\ProgramData\Foundry\Logs`.
  - Do not store secrets in app settings.
  - Initial app settings schema:
    - `schemaVersion`.
    - `appearance.theme`.
    - `localization.language`.
    - `updates.checkOnStartup`.
    - `updates.channel`.
    - `updates.feedUrl`.
    - `diagnostics.developerMode`.

- [x] **RD-015: Complete Connect/Deploy runtime configuration contracts**
  - Foundry-generated WinPE media must include complete effective runtime configuration documents for `Foundry.Connect` and `Foundry.Deploy`.
  - Do not generate sparse patch-style configuration files that only contain options changed by the user.
  - Every generated runtime document must include `schemaVersion` and every supported root section for that schema.
  - `Foundry.Connect` and `Foundry.Deploy` may remain tolerant when a file or optional property is missing for standalone/backward-compatible execution, but generated Foundry media should not rely on missing sections to mean defaults.
  - `Foundry.Connect` and `Foundry.Deploy` validation should treat generated configuration as the effective runtime contract and reject contradictory states, such as enabled Wi-Fi without the required SSID/profile data.
  - Foundry owns generation of default/effective values so WinPE runtime diagnostics remain deterministic and easy to compare in tests.

- [x] **RD-016: WinPE runtime secret protection**
  - Do not use Windows DPAPI for WinPE runtime secrets generated by Foundry because encryption occurs on the authoring Windows installation while decryption occurs later in WinPE under a different machine/user context.
  - Do not store Wi-Fi or network secrets in plaintext when a secret must be embedded for unattended WinPE execution.
  - Use an explicit secret envelope for embedded runtime secrets, backed by authenticated encryption with `System.Security.Cryptography.AesGcm`.
  - The default embedded secret format is `aes-gcm-v1` with a random per-media 256-bit key, random nonce, authentication tag, and ciphertext.
  - Store the per-media key separately from the main JSON config under the WinPE `X:\Foundry\Config\Secrets` layout and never log it.
  - Treat embedded encrypted secrets as protection against accidental disclosure and casual inspection, not as a strong boundary against an attacker who has full access to the boot media and the media key.
  - When stronger confidentiality is required, support a runtime-prompt mode where the secret is not embedded in media and `Foundry.Connect` asks the operator for the credential in WinPE.
  - Logs, summaries, validation errors, and UI diagnostics must mask both plaintext secrets and encrypted secret envelope payloads.

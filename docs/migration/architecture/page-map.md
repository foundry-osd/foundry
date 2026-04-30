# Page Map And Navigation Contract

This document defines the target WinUI shell structure for the Foundry migration.

DevWinUI remains the long-term shell baseline. Foundry pages are integrated progressively into the existing `NavigationView`, title bar, breadcrumb, settings, and content-frame conventions.

## Navigation Sections

- [ ] **General**
  - [ ] `Home`
  - [ ] `ADK`
  - [ ] `General`
  - [ ] `Start`
- [ ] **Expert**
  - [ ] `Network`
  - [ ] `Localization`
  - [ ] `Autopilot`
  - [ ] `Customization`
- [ ] **Footer**
  - [ ] `Settings`
  - [ ] `Logs`
  - [ ] `Documentation`
  - [ ] `About`

## Page Responsibilities

### Home

- [ ] Show the current Foundry readiness state.
- [ ] Highlight missing prerequisites.
- [ ] Link to `ADK` when ADK is missing or incompatible.
- [ ] Link to `Start` when the app is ready.
- [ ] Avoid duplicating detailed configuration controls from other pages.

### ADK

- [ ] Keep the page simple and operational.
- [ ] Display ADK status:
  - [ ] Missing.
  - [ ] Installed.
  - [ ] Incompatible.
- [ ] Display installed ADK version when available.
- [ ] Display required ADK version policy.
- [ ] Display WinPE Add-on status.
- [ ] Display whether Foundry can create ISO and USB media.
- [ ] Provide primary actions:
  - [ ] Install ADK.
  - [ ] Upgrade ADK.
  - [ ] Refresh status.
  - [ ] Open logs.
- [ ] Do not expose a primary uninstall action.
- [ ] Keep ADK uninstall available only as an internal upgrade implementation detail unless a future explicit advanced diagnostics requirement is approved.
- [ ] Use Foundry's own blocking progress overlay for ADK install and upgrade operations.
- [ ] Run ADK and WinPE Add-on setup in silent mode so Foundry owns the user experience.
- [ ] Do not show the native Microsoft ADK setup wizard during normal Foundry-managed installation.

### General

- [ ] Host the standard WPF configuration workflow migrated to WinUI.
- [ ] Keep only non-expert configuration options here.
- [ ] Avoid mixing expert-only pages into the standard configuration path.
- [ ] Own standard configuration fields even when they serialize into the existing expert document `general` section.
- [ ] Do not expose `General` as an Expert navigation item.

### Start

- [ ] Show a final summary of the selected configuration.
- [ ] Display readiness for:
  - [ ] ADK.
  - [ ] WinPE language.
  - [ ] Architecture.
  - [ ] ISO output path.
  - [ ] USB target.
  - [ ] Runtime payloads.
  - [ ] Driver options.
  - [ ] Network validation.
- [ ] Provide the primary commands:
  - [ ] Create ISO.
  - [ ] Create USB.
- [ ] Do not contain detailed ADK setup controls.
- [ ] Link to the relevant page when a prerequisite blocks execution.

### Expert Pages

- [ ] Keep expert workflows separate from the standard workflow.
- [ ] `Network` owns Connect provisioning, Wi-Fi profiles, 802.1X, certificates, and network validation.
- [ ] `Network` uses explicit `PasswordBox` handling for Wi-Fi and network secrets.
- [ ] `Network` never logs secrets and never exposes secrets in summary text.
- [ ] `Localization` owns WinPE language and localization-related media settings.
- [ ] `Autopilot` owns profile import, selection, validation, and embedding.
- [ ] `Customization` owns deployment customization payloads and related expert options.

### Footer Pages

- [ ] `Settings` owns app-level settings only:
  - [ ] Theme.
  - [ ] Language.
  - [ ] Update channel or update behavior.
  - [ ] Diagnostics preferences.
- [ ] `Settings` persists app-level settings through an internal `IAppSettingsService`.
- [ ] `Settings` writes to `C:\ProgramData\Foundry\Settings\appsettings.json`.
- [ ] `Settings` does not persist secrets.
- [ ] `Logs` opens or displays application logs.
- [ ] `Documentation` links to user-facing documentation.
- [ ] `About` shows product, version, repository, issue, and update information.

## Navigation Guard States

Navigation availability must be controlled by the shell, not by each page independently.

### `AdkBlocked`

Used when ADK is missing or incompatible.

- [ ] Enabled:
  - [ ] `Home`.
  - [ ] `ADK`.
  - [ ] Footer pages.
- [ ] Disabled:
  - [ ] `General`.
  - [ ] `Start`.
  - [ ] All `Expert` pages.

### `Ready`

Used when ADK is compatible and no blocking operation is running.

- [ ] Enabled:
  - [ ] `Home`.
  - [ ] `ADK`.
  - [ ] `General`.
  - [ ] `Start`.
  - [ ] All `Expert` pages.
  - [ ] Footer pages.

### `OperationRunning`

Used while ADK installation, ADK upgrade, ISO creation, USB creation, Autopilot profile import, Autopilot tenant download, or another global operation is running.

- [ ] Enabled:
  - [ ] Active operation overlay only.
- [ ] Disabled:
  - [ ] All `NavigationView` items.
  - [ ] Back navigation.
  - [ ] Settings navigation.
  - [ ] Expert navigation.

## Operation Overlay Contract

- [ ] The overlay appears above the current page.
- [ ] The overlay blocks navigation until the operation has fully completed.
- [ ] The overlay is used for:
  - [ ] ADK install.
  - [ ] ADK upgrade.
  - [ ] ISO creation.
  - [ ] USB creation.
  - [ ] Autopilot profile import.
  - [ ] Autopilot tenant download.
- [ ] The overlay displays:
  - [ ] Operation title.
  - [ ] Current step.
  - [ ] Determinate progress when available.
  - [ ] Indeterminate progress when the external process does not expose reliable progress.
  - [ ] Important status messages.
  - [ ] Final success or failure result.
- [ ] Cancellation is only exposed when the underlying operation can be cancelled safely.
- [ ] Closing the overlay manually is not allowed while the operation is running.

## ADK Installation Flow

- [ ] Download or reuse cached `adksetup.exe`.
- [ ] Download or reuse cached `adkwinpesetup.exe`.
- [ ] Install ADK Deployment Tools in silent mode.
- [ ] Install the WinPE Add-on in silent mode.
- [ ] Refresh ADK status after setup exits.
- [ ] Verify that ADK is installed.
- [ ] Verify that the installed version matches the supported version policy.
- [ ] Verify that WinPE tooling is present.
- [ ] Unlock `General`, `Start`, and `Expert` navigation only after the ADK state is compatible.

## ADK Command-Line Policy

- [ ] Use Microsoft-documented ADK command-line installation behavior.
- [ ] Use `/quiet` for Foundry-managed installation.
- [ ] Use `/features OptionId.DeploymentTools` for the ADK Deployment Tools installation.
- [ ] Keep `/norestart` behavior unless a future ADK version requires a different policy.
- [ ] Keep the WinPE Add-on as a separate installer step.
- [ ] Revalidate the official Microsoft download URLs and supported ADK version before implementing the WinUI migration.
- [ ] Treat the current WPF `10.1.26100.*` compatibility policy as the starting point, not as a permanent assumption.

## Shell Implementation Boundary

- [ ] Keep the WinUI app elevated at startup with a `requireAdministrator` app manifest during this migration.
- [ ] Do not introduce per-operation elevation in this migration.
- [ ] Put navigation availability in a shell-level guard service.
- [ ] Do not duplicate navigation lock logic inside individual pages.
- [ ] Keep page code-behind limited to WinUI-specific event glue.
- [ ] Keep business state and command eligibility in view models and application services.
- [ ] Keep ADK detection and installation logic outside UI pages.

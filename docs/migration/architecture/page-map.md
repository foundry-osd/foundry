# Page Map And Navigation Contract

This document defines the target WinUI shell structure for the Foundry migration.

DevWinUI remains the long-term shell baseline. Foundry pages are integrated progressively into the existing `NavigationView`, title bar, breadcrumb, settings, and content-frame conventions.

## DevWinUI Navigation Boundary

- [ ] Use DevWinUI `JsonNavigationService` for static navigation metadata:
  - [ ] Build `NavigationView` menu items from `Assets\NavViewMenu\AppData.json`.
  - [ ] Route `UniqueId` values through generated page mappings.
  - [ ] Populate standard and footer navigation items.
  - [ ] Localize navigation labels through `LocalizeId` and `UsexUid=true`.
  - [ ] Rebuild localized menu text through `JsonNavigationService.ReInitialize()`.
- [ ] Use DevWinUI `BreadcrumbNavigator` for breadcrumb display and page-title integration.
- [ ] Use DevWinUI `TitleBar` integration for shell back button and pane toggling.
- [ ] Do not encode Foundry runtime guard logic in custom `AppData.json` fields.
- [ ] Foundry owns runtime guard application after DevWinUI creates or recreates menu items.
- [ ] Foundry owns programmatic navigation checks so disabled menu items cannot be bypassed by search, back navigation, commands, or direct service calls.

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
  - [ ] `Documentation`
  - [ ] `About`
  - [ ] `Settings`

## Navigation Grouping Decision

- [ ] Use section headers plus direct page items for `General` and `Expert`.
- [ ] Do not use expandable or collapsible parent groups for `General` and `Expert`.
- [ ] `General` and `Expert` section labels are not navigable pages.
- [ ] All pages under those sections remain directly visible and directly clickable.
- [ ] Footer items remain in the `NavigationView.FooterMenuItems` area when supported by DevWinUI.

Target visual shape:

```text
General
  Home
  ADK
  General
  Start

Expert
  Network
  Localization
  Autopilot
  Customization

Footer
  Documentation
  About
  Settings
```

## Page Responsibilities

### Home

- [ ] Act as a simple welcome/onboarding page, not a dense operational dashboard.
- [ ] Keep visible content intentionally limited:
  - [ ] Short welcome text.
  - [ ] One lightweight ADK status card.
  - [ ] A small set of clear action buttons.
- [ ] The ADK status card shows only the essential state:
  - [ ] Compatible/ready state is visibly positive, for example green.
  - [ ] Missing or incompatible state is visibly non-ready and links to `ADK`.
  - [ ] Detailed ADK version, policy, and install diagnostics stay on the `ADK` page.
- [ ] Link to `ADK` when ADK is missing or incompatible.
- [ ] Link to `General` when standard media defaults need configuration.
- [ ] Link to `Start` when the app is ready to create media.
- [ ] Link to `Documentation` as a secondary onboarding action.
- [ ] Do not link to Expert pages from Home unless a future explicit onboarding need is approved.
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
  - [ ] Open logs from the ADK page diagnostics/action area, not from a dedicated footer navigation item.
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
- [ ] Own WinPE boot language selection used by ISO/USB media creation.
- [ ] WinPE boot language is a media/boot image setting, not the expert `Localization` page.
- [ ] Do not expose `General` as an Expert navigation item.

### Start

- [ ] Show a final summary of the selected configuration.
- [ ] Display readiness for:
  - [ ] ADK.
  - [ ] WinPE boot language selected from the `General` page.
  - [ ] Architecture.
  - [ ] ISO output path.
  - [ ] USB target.
  - [ ] Runtime payloads.
  - [ ] Driver options.
  - [ ] Network validation.
- [ ] During Phase 13, expose preflight and dry-run summaries only.
- [ ] Keep final ISO/USB execution disabled through the Deploy, Connect/network, secret, runtime payload, and optional Autopilot/customization readiness phases; command enablement belongs to Phase 16.E after those readiness signals are real.
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
- [ ] `Localization` owns OS deployment localization settings consumed by `Foundry.Deploy`:
  - [ ] Available deployment languages.
  - [ ] Visible deployment languages.
  - [ ] Default deployment language override.
  - [ ] Default deployment time zone.
  - [ ] Single visible deployment language behavior.
- [ ] `Localization` does not own WinPE boot language selection.
- [ ] `Autopilot` owns profile import, selection, validation, and embedding.
- [ ] `Customization` owns deployment customization payloads and related expert options.

### Footer Pages

- [ ] `Settings` owns app-level settings only:
  - [ ] Theme.
  - [ ] Application UI language.
  - [ ] Update channel or update behavior.
  - [ ] Diagnostics preferences.
- [ ] `Settings` persists app-level settings through an internal `IAppSettingsService`.
- [ ] `Settings` writes to `C:\ProgramData\Foundry\Settings\appsettings.json`.
- [ ] `Settings` does not persist secrets.
- [ ] `Settings` language changes affect Foundry UI localization only, not WinPE boot language and not OS deployment languages.
- [ ] Do not expose a dedicated `Logs` footer page.
- [ ] Log-folder access remains available from diagnostics/settings surfaces.
- [ ] `Documentation` links to user-facing documentation.
- [ ] `About` shows product, version, repository, issue, and update information.

## Update Banner Contract

- [x] Use a global top-shell `InfoBar` banner for app update availability.
- [x] The banner is hosted by the shell, not by `AppData.json`.
- [x] The banner appears above the current page content and remains visible across navigation.
- [x] The banner is non-modal and does not block startup.
- [x] The banner action opens the update settings page or a dedicated update view.
- [x] Download and restart remain explicit user actions.
- [x] Startup update checks, manual update checks, the shell banner, and the settings update page share the same latest update state.
- [x] If startup finds an update before the settings page is opened, the settings page shows that update state immediately on load.
- [x] If startup finds an update while the settings page is already open, the settings page updates without requiring the user to click `Check for updates`.
- [x] Do not add a Windows toast notification in Phase 11.

## Navigation Guard States

Navigation availability must be controlled by the shell, not by each page independently.

After any DevWinUI navigation rebuild, including runtime language switching through `JsonNavigationService.ReInitialize()`, the shell must reapply the current Foundry navigation guard state.

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
  - [ ] Title bar back navigation.
  - [ ] Search-driven navigation.
  - [ ] Programmatic navigation commands outside the active operation.

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
- [ ] Treat the WPF `10.1.26100.*` compatibility policy as a starting point, not as a permanent assumption.
- [ ] Require `10.1.26100.2454+` for the general Windows 11 24H2/25H2 ADK track.
- [ ] Do not allow the Windows 11 26H1 Arm64 `10.1.28000.x` ADK track in Foundry.
- [ ] Display that Microsoft recommends applying the latest ADK servicing patch for the detected target track.

## Shell Implementation Boundary

- [ ] Keep the WinUI app elevated at startup with a `requireAdministrator` app manifest during this migration.
- [ ] Do not introduce per-operation elevation in this migration.
- [ ] Do not migrate the old WPF menu bar.
- [ ] Add only product-relevant DevWinUI shell entry points; do not recreate WPF menu commands 1:1.
- [ ] Put navigation availability in a shell-level guard service.
- [ ] Do not duplicate navigation lock logic inside individual pages.
- [ ] Keep page code-behind limited to WinUI-specific event glue.
- [ ] Keep business state and command eligibility in view models and application services.
- [ ] Keep ADK detection and installation logic outside UI pages.

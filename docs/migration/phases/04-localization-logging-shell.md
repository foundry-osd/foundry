# Localization, Logging, And Shell Phases

## Phase 9: Localization Migration

**Priority:** medium-high.

**Goal:** replace WPF `.resx`/indexer binding behavior in the main `Foundry` app with a `.resw` WinUI localization system.

- [ ] **9.1** Inventory current localization keys:
  - [ ] `archive\Foundry.WpfReference\Resources\AppStrings.resx`.
  - [ ] `archive\Foundry.WpfReference\Resources\AppStrings.fr-FR.resx`.
- [ ] **9.2** Use the decided target resource format:
  - [ ] Use `.resw` for all migrated WinUI `Foundry` UI text.
  - [ ] Use `.resw` for view-model-facing user-visible text in the WinUI `Foundry` app.
  - [ ] Do not keep `.resx` localization in the migrated WinUI `Foundry` app.
  - [ ] Keep `.resx` only in WPF `Foundry.Connect` and `Foundry.Deploy`.
- [ ] **9.3** Define localization layers:
  - [ ] UI resources for XAML text.
  - [ ] ViewModel text service for computed labels/status.
  - [ ] Core codes, values, or invariant diagnostics that can be mapped to `.resw` text by the WinUI app.
- [ ] **9.4** Implement missing-key behavior:
  - [ ] Development mode logs missing keys.
  - [ ] Production mode falls back safely.
- [ ] **9.5** Implement culture switching:
  - [ ] Menu/settings command updates the selected language.
  - [ ] ViewModels refresh computed strings.
  - [ ] Pages reload or rebind localized text.
- [ ] **9.6** Migrate languages:
  - [ ] `en-US`.
  - [ ] `fr-FR`.
- [ ] **9.7** Migrate supported culture catalog tests.
- [ ] **9.8** Commit:

```powershell
git commit -m "feat: migrate foundry localization to winui"
```

**Validation**

- [ ] **9.9** Launch app in English.
- [ ] **9.10** Launch app in French.
- [ ] **9.11** Switch language at runtime.
- [ ] **9.12** Confirm computed labels update.
- [ ] **9.13** Confirm missing keys are visible during development.

## Phase 10: Logging Migration

**Priority:** medium-high.

**Goal:** preserve production diagnostics across the WinUI startup and update model.

- [ ] **10.1** Use the approved ProgramData log directory:
  - [ ] `C:\ProgramData\Foundry\Logs`.
  - [ ] Do not use `%LocalAppData%\Foundry\Logs` for the WinUI `Foundry` app.
- [ ] **10.2** Use a single active host log file for the initial WinUI migration:
  - [ ] `C:\ProgramData\Foundry\Logs\Foundry.log`.
  - [ ] Add rolling only if implementation or test evidence shows the active log can grow too large.
- [ ] **10.3** Preserve UTC timestamp enrichment.
- [ ] **10.4** Include:
  - [ ] App version.
  - [ ] Runtime identifier.
  - [ ] Process architecture.
  - [ ] Update/install context.
  - [ ] Source context.
- [ ] **10.5** Configure Serilog before app startup work.
- [ ] **10.6** Add logging for:
  - [ ] App startup.
  - [ ] Windows App SDK runtime initialization.
  - [ ] Velopack first run/update flow.
  - [ ] ADK detection.
  - [ ] ISO/USB build start and completion.
  - [ ] Bootstrap payload resolution.
- [ ] **10.7** Keep log folder command in UI.
- [ ] **10.7.1** Log folder command opens `C:\ProgramData\Foundry\Logs`.
- [ ] **10.8** Commit:

```powershell
git commit -m "refactor: migrate foundry logging for winui"
```

**Validation**

- [ ] **10.9** Confirm log file exists after normal startup.
- [ ] **10.10** Confirm startup failures are logged.
- [ ] **10.11** Confirm update failures are logged.
- [ ] **10.12** Confirm media creation logs remain readable.

## Phase 11: Shell, Navigation, And App Settings

**Priority:** medium.

**Goal:** keep DevWinUI as the long-term shell baseline and progressively plug real Foundry workflows into it.

- [ ] **11.1** Keep DevWinUI navigation, settings, title bar, breadcrumb, and shell conventions as the long-term Foundry shell baseline.
- [ ] **11.2** Do not port the old WPF main window layout 1:1.
- [ ] **11.3** Replace prototype menu JSON with Foundry-specific entries.
- [ ] **11.4** Confirm generated page mappings are deterministic.
- [ ] **11.5** Add Foundry pages incrementally without blocking unrelated business logic extraction.
- [ ] **11.6** Define first-level pages:
  - [ ] General section: `Home`, `ADK`, `General`, `Start`.
  - [ ] Expert section: `Network`, `Localization`, `Autopilot`, `Customization`.
  - [ ] Footer section: `Settings`, `Logs`, `Documentation`, `About`.
  - [ ] **11.6.1** Use the target page map from [Page Map And Navigation Contract](../architecture/page-map.md).
  - [ ] **11.6.2** Implement shell-level navigation guard states:
    - [ ] `AdkBlocked`.
    - [ ] `Ready`.
    - [ ] `OperationRunning`.
  - [ ] **11.6.3** When ADK is missing or incompatible, allow only:
    - [ ] `Home`.
    - [ ] `ADK`.
    - [ ] Footer pages.
  - [ ] **11.6.4** When ADK is missing or incompatible, disable:
    - [ ] `General`.
    - [ ] `Start`.
    - [ ] All `Expert` pages.
  - [ ] **11.6.5** When a global operation is running, disable:
    - [ ] All `NavigationView` items.
    - [ ] Back navigation.
    - [ ] Settings navigation.
  - [ ] **11.6.6** Add blocking operation overlay support:
    - [ ] ADK install.
    - [ ] ADK upgrade.
    - [ ] ISO creation.
    - [ ] USB creation.
  - [ ] **11.6.7** Ensure the operation overlay remains visible and blocks navigation until the operation fully completes.
- [ ] **11.7** Move old WPF menu commands into WinUI shell commands:
  - [ ] Import expert configuration.
  - [ ] Export expert configuration.
  - [ ] Export deploy configuration.
  - [ ] Open logs.
  - [ ] Documentation.
  - [ ] GitHub repository.
  - [ ] GitHub issues.
  - [ ] Check for updates.
  - [ ] About.
- [ ] **11.8** Keep code-behind limited to WinUI events and navigation glue.
- [ ] **11.8.1** Replace DevWinUI prototype AppData paths with Foundry ProgramData paths:
  - [ ] `C:\ProgramData\Foundry\Settings\appsettings.json`.
  - [ ] `C:\ProgramData\Foundry\Logs`.
  - [ ] `C:\ProgramData\Foundry\Cache`.
  - [ ] `C:\ProgramData\Foundry\Workspaces`.
  - [ ] `C:\ProgramData\Foundry\Temp`.
- [ ] **11.8.2** Remove DevWinUI prototype `nucs.JsonSettings` app settings plumbing from the WinUI `Foundry` app.
- [ ] **11.8.3** Implement initial `appsettings.json` schema:
  - [ ] `schemaVersion`.
  - [ ] `appearance.theme`.
  - [ ] `localization.language`.
  - [ ] `updates.checkOnStartup`.
  - [ ] `updates.channel`.
  - [ ] `updates.feedUrl`.
  - [ ] `diagnostics.developerMode`.
  - [ ] No secrets.
  - [ ] No workflow or export configuration.
- [ ] **11.9** Commit:

```powershell
git commit -m "feat: add foundry winui shell navigation"
```

**Validation**

- [ ] **11.10** Navigate to every enabled page in `Ready` state.
- [ ] **11.11** Confirm settings pages load with real view models.
- [ ] **11.12** Confirm app window title/icon are correct.
- [ ] **11.13** Confirm theme switching works.
- [ ] **11.14** Confirm `AdkBlocked` state disables `General`, `Start`, and all `Expert` pages.
- [ ] **11.15** Confirm `OperationRunning` state blocks navigation until the operation completes.

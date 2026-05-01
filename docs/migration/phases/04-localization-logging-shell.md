# Localization, Logging, And Shell Phases

## Phase 9: Localization Migration

**Priority:** medium-high.

**Goal:** replace WPF `.resx`/indexer binding behavior in the main `Foundry` app with a `.resw` WinUI localization system.

- [ ] **9.1** Inventory current localization keys:
  - [ ] `archive\Foundry.WpfReference\Resources\AppStrings.resx`.
  - [ ] `archive\Foundry.WpfReference\Resources\AppStrings.fr-FR.resx`.
  - [ ] Reuse the proven reference pattern from `E:\Github\Bucket_v2_old`:
    - [ ] `src\Bucket.Core\Services\LocalizationService.cs`.
    - [ ] `src\Bucket.App\Assets\NavViewMenu\AppData.json`.
    - [ ] `src\Bucket.App\Strings\en-US\Resources.resw`.
    - [ ] `src\Bucket.App\Strings\fr-FR\Resources.resw`.
    - [ ] `src\Bucket.App\Views\Settings\GeneralSettingPage.xaml.cs`.
    - [ ] `src\Bucket.App\Views\MainWindow.xaml.cs`.
  - [ ] DevWinUI prototype shell text:
    - [ ] `src\Foundry\Assets\NavViewMenu\AppData.json`.
    - [ ] `dev:BreadcrumbNavigator.PageTitle` and `BreadCrumbHeader` values.
    - [ ] DevWinUI settings cards, title bar search text, tooltips, badges, and landing page strings.
- [ ] **9.2** Use the decided target resource format:
  - [ ] Use `.resw` for all migrated WinUI `Foundry` UI text.
  - [ ] Use `.resw` for view-model-facing user-visible text in the WinUI `Foundry` app.
  - [ ] Do not keep `.resx` localization in the migrated WinUI `Foundry` app.
  - [ ] Keep `.resx` only in WPF `Foundry.Connect` and `Foundry.Deploy`.
  - [ ] Add `.resw` files as MSBuild `AdditionalFiles` if required by `DevWinUI.SourceGenerator`.
- [ ] **9.3** Define localization layers:
  - [ ] UI resources for XAML text.
  - [ ] ViewModel text service for computed labels/status.
  - [ ] Core codes, values, or invariant diagnostics that can be mapped to `.resw` text by the WinUI app.
  - [ ] Use `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` with a language-specific `ResourceContext` for view-model-facing string lookup.
  - [ ] Do not rely only on `ResourceLoader` for runtime language changes, because the old DevWinUI project used `ResourceManager` + `ResourceContext.QualifierValues["Language"]` successfully for hot switching.
  - [ ] Keep `Foundry.Core` free of Windows App SDK resource APIs; the WinUI app owns `.resw` lookup and localized display strings.
  - [ ] DevWinUI navigation metadata:
    - [ ] Use `LocalizeId` and `UsexUid=true` in `AppData.json` for NavigationView groups/items when supported by DevWinUI.
    - [ ] Use resource keys shaped like the proven reference pattern, for example `Nav_HomeKey.Title`.
    - [ ] Keep literal `Title` values only as fallback/default design-time text.
    - [ ] Do not invent per-language JSON files or per-language `Title` maps unless DevWinUI localization support proves insufficient.
  - [ ] DevWinUI breadcrumb and settings navigation text:
    - [ ] Replace hardcoded `BreadCrumbHeader` and settings-card strings with resource-backed values.
    - [ ] Ensure generated breadcrumb mappings and runtime navigation parameters receive localized text.
- [ ] **9.4** Implement missing-key behavior:
  - [ ] Development mode logs missing keys.
  - [ ] Production mode falls back safely.
  - [ ] Invalid persisted language values fall back to `en-US` and are rewritten to `appsettings.json`.
- [ ] **9.5** Implement culture switching:
  - [ ] Initialize localization before `MainWindow.InitializeComponent()` and before `JsonNavigationService.ConfigureJsonFile(...)`.
  - [ ] Menu/settings command updates the selected language.
  - [ ] Persist the selected BCP-47 language tag to `C:\ProgramData\Foundry\Settings\appsettings.json`.
  - [ ] Set `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` during startup before localized resources are loaded.
  - [ ] Set `ApplicationLanguages.PrimaryLanguageOverride` again when the user changes language at runtime.
  - [ ] Update the active `ResourceContext.QualifierValues["Language"]` when the user changes language at runtime.
  - [ ] Expose a language change event from `IApplicationLocalizationService`.
  - [ ] ViewModels refresh computed strings.
  - [ ] Pages reload or rebind localized text.
  - [ ] DevWinUI NavigationView groups/items, breadcrumbs, settings cards, title bar search text, and landing page strings refresh without restarting the app.
  - [ ] Reinitialize DevWinUI `JsonNavigationService` after runtime language changes, following the old `Bucket_v2_old` pattern that calls `JsonNavigationService.ReInitialize()`.
  - [ ] Add a controlled Foundry wrapper around DevWinUI navigation refresh if direct `ReInitialize()` is not sufficient or becomes version-sensitive.
  - [ ] Do not require an application restart for language changes.
  - [ ] If a WinUI resource was already loaded before the language change, explicitly reload or rebind that UI surface after changing `PrimaryLanguageOverride`.
- [ ] **9.6** Migrate languages:
  - [ ] `en-US`.
  - [ ] `fr-FR`.
- [ ] **9.7** Migrate supported culture catalog tests.
- [ ] **9.7.1** Verify DevWinUI localization behavior against current DevWinUI docs/source before implementation:
  - [ ] Confirm `LocalizeId` resource-key naming requirements.
  - [ ] Confirm `UsexUid=true` behavior for NavigationView groups and items.
  - [ ] Confirm whether `JsonNavigationService.ReInitialize()` still refreshes localized navigation items in the current DevWinUI package version.
  - [ ] Document any DevWinUI limitation that requires an app-owned refresh workaround.
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
- [ ] **9.14** Confirm DevWinUI NavigationView group/item labels update after runtime language switch without restarting.
- [ ] **9.15** Confirm breadcrumbs, settings cards, title bar search text, and landing page strings update after runtime language switch without restarting.

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
  - [x] Startup and manual update check elapsed time.
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
- [ ] **11.7.1** Add shell update notification behavior:
  - [ ] Show a non-blocking update available banner when startup update check returns `UpdateAvailable`.
  - [ ] Do not interrupt startup with a modal `ContentDialog`.
  - [ ] Banner action opens the update settings page or dedicated update view.
  - [ ] Download/restart remains an explicit user action with confirmation.
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

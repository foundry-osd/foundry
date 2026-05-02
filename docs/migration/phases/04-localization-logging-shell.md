# Localization, Logging, And Shell Phases

## Phase 9: Localization Migration

**Priority:** medium-high.

**Goal:** replace WPF `.resx`/indexer binding behavior in the main `Foundry` app with a `.resw` WinUI localization system.

- [x] **9.1** Inventory current localization keys:
  - [x] `archive\Foundry.WpfReference\Resources\AppStrings.resx`.
  - [x] `archive\Foundry.WpfReference\Resources\AppStrings.fr-FR.resx`.
  - [x] Reuse the proven reference pattern from `E:\Github\Bucket_v2_old`:
    - [x] `src\Bucket.Core\Services\LocalizationService.cs`.
    - [x] `src\Bucket.App\Assets\NavViewMenu\AppData.json`.
    - [x] `src\Bucket.App\Strings\en-US\Resources.resw`.
    - [x] `src\Bucket.App\Strings\fr-FR\Resources.resw`.
    - [x] `src\Bucket.App\Views\Settings\GeneralSettingPage.xaml.cs`.
    - [x] `src\Bucket.App\Views\MainWindow.xaml.cs`.
  - [x] DevWinUI prototype shell text:
    - [x] `src\Foundry\Assets\NavViewMenu\AppData.json`.
    - [x] `dev:BreadcrumbNavigator.PageTitle` and `BreadCrumbHeader` values.
    - [x] DevWinUI settings cards, title bar search text, tooltips, badges, and landing page strings.
- [x] **9.2** Use the decided target resource format:
  - [x] Use `.resw` for all migrated WinUI `Foundry` UI text.
  - [x] Use `.resw` for view-model-facing user-visible text in the WinUI `Foundry` app.
  - [x] Do not keep `.resx` localization in the migrated WinUI `Foundry` app.
  - [x] Keep `.resx` only in WPF `Foundry.Connect` and `Foundry.Deploy`.
  - [x] Add `.resw` files as MSBuild `AdditionalFiles` if required by `DevWinUI.SourceGenerator`.
- [x] **9.3** Define localization layers:
  - [x] UI resources for XAML text.
  - [x] ViewModel text service for computed labels/status.
  - [x] Core codes, values, or invariant diagnostics that can be mapped to `.resw` text by the WinUI app.
  - [x] Use `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` with a language-specific `ResourceContext` for view-model-facing string lookup.
  - [x] Do not rely only on `ResourceLoader` for runtime language changes, because the old DevWinUI project used `ResourceManager` + `ResourceContext.QualifierValues["Language"]` successfully for hot switching.
  - [x] Keep `Foundry.Core` free of Windows App SDK resource APIs; the WinUI app owns `.resw` lookup and localized display strings.
  - [x] DevWinUI navigation metadata:
    - [x] Use `LocalizeId` and `UsexUid=true` in `AppData.json` for NavigationView groups/items when supported by DevWinUI.
    - [x] Use resource keys shaped like the proven reference pattern, for example `Nav_HomeKey.Title`.
    - [x] Keep literal `Title` values only as fallback/default design-time text.
    - [x] Do not invent per-language JSON files or per-language `Title` maps unless DevWinUI localization support proves insufficient.
  - [x] DevWinUI breadcrumb and settings navigation text:
    - [x] Replace hardcoded `BreadCrumbHeader` and settings-card strings with resource-backed values.
    - [x] Ensure generated breadcrumb mappings and runtime navigation parameters receive localized text.
- [x] **9.4** Implement missing-key behavior:
  - [x] Development mode logs missing keys.
  - [x] Production mode falls back safely.
  - [x] Invalid persisted language values fall back to `en-US` and are rewritten to `appsettings.json`.
- [x] **9.5** Implement culture switching:
  - [x] Initialize localization before `MainWindow.InitializeComponent()` and before `JsonNavigationService.ConfigureJsonFile(...)`.
  - [x] Menu/settings command updates the selected language.
  - [x] Persist the selected BCP-47 language tag to `C:\ProgramData\Foundry\Settings\appsettings.json`.
  - [x] Set `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` during startup before localized resources are loaded.
  - [x] Set `ApplicationLanguages.PrimaryLanguageOverride` again when the user changes language at runtime.
  - [x] Update the active `ResourceContext.QualifierValues["Language"]` when the user changes language at runtime.
  - [x] Expose a language change event from `IApplicationLocalizationService`.
  - [x] ViewModels refresh computed strings.
  - [x] Pages reload or rebind localized text.
  - [x] DevWinUI NavigationView groups/items, breadcrumbs, settings cards, title bar search text, and landing page strings refresh without restarting the app.
  - [x] Reinitialize DevWinUI `JsonNavigationService` after runtime language changes, following the old `Bucket_v2_old` pattern that calls `JsonNavigationService.ReInitialize()`.
  - [x] Add a controlled Foundry wrapper around DevWinUI navigation refresh if direct `ReInitialize()` is not sufficient or becomes version-sensitive.
  - [x] Do not require an application restart for language changes.
  - [x] If a WinUI resource was already loaded before the language change, explicitly reload or rebind that UI surface after changing `PrimaryLanguageOverride`.
- [x] **9.6** Migrate languages:
  - [x] `en-US`.
  - [x] `fr-FR`.
- [x] **9.7** Migrate supported culture catalog tests.
- [x] **9.7.1** Verify DevWinUI localization behavior against current DevWinUI docs/source before implementation:
  - [x] Confirm `LocalizeId` resource-key naming requirements.
  - [x] Confirm `UsexUid=true` behavior for NavigationView groups and items.
  - [x] Confirm whether `JsonNavigationService.ReInitialize()` still refreshes localized navigation items in the current DevWinUI package version.
  - [x] Document any DevWinUI limitation that requires an app-owned refresh workaround.
- [x] **9.8** Commit:

```powershell
git commit -m "feat: migrate foundry localization to winui"
```

**Validation**

- [x] **9.9** Launch app in English.
- [x] **9.10** Launch app in French.
- [x] **9.11** Switch language at runtime.
- [x] **9.12** Confirm computed labels update.
- [x] **9.13** Confirm missing keys are visible during development.
- [x] **9.14** Confirm DevWinUI NavigationView group/item labels update after runtime language switch without restarting.
- [x] **9.15** Confirm breadcrumbs, settings cards, title bar search text, and landing page strings update after runtime language switch without restarting.

## Phase 10: Logging Migration

**Priority:** medium-high.

**Goal:** preserve production diagnostics across the WinUI startup and update model.

- [x] **10.1** Use the approved ProgramData log directory:
  - [x] `C:\ProgramData\Foundry\Logs`.
  - [x] Do not use `%LocalAppData%\Foundry\Logs` for the WinUI `Foundry` app.
  - [x] Validate the existing `Constants.LogDirectoryPath` and `Constants.LogFilePath` implementation before changing it.
- [x] **10.2** Use a single active host log file for the initial WinUI migration:
  - [x] `C:\ProgramData\Foundry\Logs\Foundry.log`.
  - [x] Add rolling only if implementation or test evidence shows the active log can grow too large.
- [x] **10.3** Use a readable timestamp in the human log:
  - [x] Keep local timestamp with offset in `Foundry.log`.
  - [x] Do not duplicate UTC metadata on every line.
- [x] **10.4** Include:
  - [x] App version in startup metadata.
  - [x] Runtime identifier in startup metadata.
  - [x] Process architecture in startup metadata.
  - [x] Update/install context in relevant update and Velopack events.
  - [x] Concise source context.
- [x] **10.5** Configure bootstrap Serilog before app startup work:
  - [x] Initialize logging in `Program.Main` before `VelopackApp.Build().Run()`.
  - [x] Log Velopack startup/update hook entry and completion.
  - [x] Log WinRT COM wrapper initialization start and completion.
  - [x] Keep final app logger configuration consistent after DI/settings are available.
- [x] **10.5.1** Register unhandled exception handlers as early as possible:
  - [x] Cover failures before `App` host creation where feasible.
  - [x] Preserve AppDomain, WinUI, and unobserved task exception logging.
- [x] **10.5.2** Implement source-context logging:
  - [x] Do not inject the same untyped global `Serilog.ILogger` when class-level source context is expected.
  - [x] Use contextual loggers such as `Log.ForContext<T>()`, a typed logger factory, or a Serilog-backed Microsoft logging integration.
  - [x] Confirm a concise source context/component is visible in log output.
- [x] **10.5.3** Implement runtime log-level policy:
  - [x] Use a Serilog `LoggingLevelSwitch` or equivalent logger reconfiguration.
  - [x] Default mode writes `Information`, `Warning`, `Error`, and `Fatal`.
  - [x] Developer mode adds `Debug`.
  - [x] Switching `diagnostics.developerMode` in Settings updates the active logging level without restarting when feasible.
  - [x] Do not use `Verbose` unless a future subsystem proves it needs trace-level diagnostics.
- [x] **10.6** Add logging for:
  - [x] App startup.
  - [x] Windows App SDK runtime initialization.
  - [x] Velopack first run/update flow.
  - [x] Startup and manual update check elapsed time.
  - [x] Debug-level diagnostic details where useful, emitted only when `diagnostics.developerMode` is enabled in Settings.
- [ ] **10.6.1** Define future workflow logging contracts without blocking Phase 10 on unimplemented workflows:
  - [ ] ADK detection logs required when ADK service/page work is implemented.
  - [ ] ISO/USB build start, progress, completion, cancellation, and failure logs required when media creation work is implemented.
  - [ ] Bootstrap payload resolution logs required when payload resolution work is implemented.
- [x] **10.7** Keep log folder command in UI.
- [x] **10.7.1** Log folder command opens `C:\ProgramData\Foundry\Logs`.
- [x] **10.7.2** Remove unused logging dependencies unless they serve a configured sink:
  - [x] Remove `Serilog.Sinks.Console` from the WinUI project if no console sink is intentionally configured.
- [x] **10.8** Commit:

```powershell
git commit -m "refactor: migrate foundry logging for winui"
```

**Validation**

- [x] **10.9** Confirm log file exists after normal startup.
- [x] **10.10** Confirm startup failures are logged.
- [x] **10.11** Confirm update failures are logged.
- [ ] **10.12** Confirm media creation logs remain readable.
  - Deferred until Phase 12 implements ISO/USB media creation in WinUI.
  - Complete this through Phase 12 validation item **12.16**.
- [x] **10.13** Confirm `Debug` events are absent when `diagnostics.developerMode=false`.
- [x] **10.14** Confirm `Debug` events are written when `diagnostics.developerMode=true`.
- [x] **10.15** Confirm concise source context appears for app services.

## Phase 11: Shell, Navigation, And App Settings

**Priority:** medium.

**Goal:** keep DevWinUI as the long-term shell baseline and progressively plug real Foundry workflows into it.

- [ ] **11.1** Keep DevWinUI navigation, settings, title bar, breadcrumb, and shell conventions as the long-term Foundry shell baseline.
  - [ ] DevWinUI remains responsible for JSON-driven `NavigationView` item creation through `JsonNavigationService`.
  - [ ] DevWinUI remains responsible for `TitleBar`, `BreadcrumbNavigator`, `Settings` item integration, search suggestions, and page navigation glue where the existing service supports it.
  - [ ] Foundry owns runtime business state decisions that DevWinUI JSON does not model, including ADK readiness, operation locks, update banners, and command eligibility.
- [ ] **11.2** Do not port the old WPF main window layout 1:1.
- [ ] **11.3** Replace prototype menu JSON with Foundry-specific entries.
  - [ ] Use `Assets\NavViewMenu\AppData.json` as the source for DevWinUI top-level navigation.
  - [ ] Use section headers plus direct page items for the main navigation:
    - [ ] `General` and `Expert` are visual section headers.
    - [ ] `General` and `Expert` are not clickable parent pages.
    - [ ] `General` and `Expert` are not expandable/collapsible groups.
    - [ ] Pages remain directly visible under their section header.
  - [ ] Use DevWinUI-supported schema fields:
    - [ ] `Groups`.
    - [ ] `Items`.
    - [ ] `UniqueId`.
    - [ ] `Title` as fallback text.
    - [ ] `LocalizeId`.
    - [ ] `UsexUid=true`.
    - [ ] `ImagePath` or `IconGlyph`.
    - [ ] `IsFooterNavigationViewItem`.
    - [ ] `ShowItemsWithoutGroup`.
    - [ ] `IsExpanded`.
    - [ ] `HideGroup`.
    - [ ] `HideNavigationViewItem`.
    - [ ] `IncludedInBuild` only for compile/build availability, not runtime business blocking.
  - [ ] Do not depend on unsupported JSON-only runtime state such as `RequiresAdk`, `OperationLocked`, or per-state enabled rules.
  - [ ] Keep runtime navigation state in Foundry services instead of inventing custom DevWinUI JSON schema fields.
- [ ] **11.4** Confirm generated page mappings are deterministic.
  - [ ] Confirm `NavigationPageMappings.PageDictionary` contains every `UniqueId` referenced by `AppData.json`.
  - [ ] Confirm `BreadcrumbPageMappings.PageDictionary` contains every shell page and settings page that can appear in breadcrumbs.
  - [ ] Confirm each page has one stable `UniqueId`; use DevWinUI `Parameter` only when the same page type intentionally represents multiple logical pages.
- [ ] **11.5** Add Foundry pages incrementally without blocking unrelated business logic extraction.
- [ ] **11.6** Define first-level pages:
  - [ ] General section header with direct items: `Home`, `ADK`, `General`, `Start`.
  - [ ] Expert section header with direct items: `Network`, `Localization`, `Autopilot`, `Customization`.
  - [ ] Footer section in this order: `Documentation`, `About`, `Settings`.
  - [ ] Do not add a `Logs` footer navigation item.
  - [ ] Keep log-folder access inside diagnostics/settings surfaces instead of exposing a dedicated navigation page.
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
    - [ ] Title bar back navigation.
    - [ ] Search-driven navigation.
  - [ ] **11.6.6** Add blocking operation overlay support:
    - [ ] ADK install.
    - [ ] ADK upgrade.
    - [ ] ISO creation.
    - [ ] USB creation.
  - [ ] **11.6.7** Ensure the operation overlay remains visible and blocks navigation until the operation fully completes.
  - [ ] **11.6.8** Apply navigation guards after every DevWinUI navigation refresh:
    - [ ] Initial `JsonNavigationService.ConfigureJsonFile(...)`.
    - [ ] Runtime localization refresh through `JsonNavigationService.ReInitialize()`.
    - [ ] Any future AppData/menu rebuild.
  - [ ] **11.6.9** Do not rely only on `NavigationViewItem.IsEnabled` for operation blocking:
    - [ ] Prevent programmatic navigation through a Foundry-owned navigation facade or guard check.
    - [ ] Prevent search result navigation when `OperationRunning`.
    - [ ] Prevent back navigation when `OperationRunning`.
    - [ ] Keep the active operation page/overlay visible until completion.
- [ ] **11.7** Do not migrate the old WPF menu bar.
  - [ ] Remove any expectation of a 1:1 WPF menu command port.
  - [ ] Add only WinUI shell entry points that still make product sense in the DevWinUI shell:
    - [ ] Documentation.
    - [ ] GitHub repository.
    - [ ] GitHub issues.
    - [ ] Check for updates.
    - [ ] About.
  - [ ] Do not add a dedicated Logs navigation button; use the existing settings diagnostics/log-folder command.
  - [ ] Keep import/export configuration actions for later workflow phases instead of adding them as menu-bar equivalents:
    - [ ] Import expert configuration is handled by Phase 14.
    - [ ] Export expert configuration is handled by Phase 14.
    - [ ] Export deploy configuration is handled by Phase 14.
- [ ] **11.7.1** Add shell update notification behavior:
  - [ ] Use a global top-shell WinUI `InfoBar` banner pattern inspired by UniGetUI's `UpdatesBanner`.
  - [ ] Host the banner in the shell, above the page content and shared across pages.
  - [ ] Show a non-blocking update available banner when startup update check returns `UpdateAvailable`.
  - [ ] Do not interrupt startup with a modal `ContentDialog`.
  - [ ] Banner action opens the update settings page or dedicated update view.
  - [ ] Download/restart remains an explicit user action with confirmation.
  - [ ] Keep the banner outside DevWinUI `AppData.json`; it is runtime state, not static navigation metadata.
  - [ ] Keep the banner persistent until the user dismisses it, opens update details, or the update state changes.
  - [ ] Do not add Windows toast activation in Phase 11; revisit only if background update notifications become a product requirement.
  - [ ] Share update state between startup checks, shell banner, and settings UI:
    - [ ] Startup check publishes the latest `ApplicationUpdateCheckResult`.
    - [ ] Manual check publishes the latest `ApplicationUpdateCheckResult`.
    - [ ] Settings update page reads the latest published state when it loads.
    - [ ] Settings update page updates automatically when startup check completes while the page is open.
    - [ ] The shell banner and settings page use the same pending update state, so the user does not need to click `Check for updates` again after a startup check found an update.
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
- [ ] **11.16** Confirm search suggestions cannot navigate while `OperationRunning`.
- [ ] **11.17** Confirm language switching reinitializes DevWinUI navigation and then reapplies the current Foundry navigation guard state.
- [ ] **11.18** Confirm unsupported custom `AppData.json` fields are not required for Phase 11 behavior.

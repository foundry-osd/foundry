# Compatibility, UI Review, Cleanup, And Cutover Phases

## Phase 17: End-To-End Generated Media Validation

**Priority:** high before cutover.

**Goal:** prove the full Foundry solution works from WinUI configuration through ISO/USB generation, WinPE boot, `Foundry.Connect` execution, and `Foundry.Deploy` execution.

**Prerequisites and boundary:** Run Phase 17 after Phase 16.E final media command enablement is complete and merged. Phase 17 is an end-to-end validation phase, not a broad refactor phase. Only commit targeted fixes when the generated-media tests expose a concrete incompatibility in configuration contracts, media layout, bootstrapping, Connect, Deploy, logging, or operator flow. Do not add backward-compatibility fallbacks for obsolete WPF-era paths or schemas; fix the generated contract and runtime consumers directly.

- [ ] **17.1** Prepare a clean validation matrix:
  - [ ] Record ADK and WinPE Add-on version, Windows host build, target architecture, signature mode, and generated media paths.
  - [ ] Record whether each run uses ISO, USB, VM boot, physical boot, standard configuration, expert configuration, Wi-Fi/WinRE, secrets, Autopilot, and customization.
  - [ ] Keep disposable generated artifacts and logs until the phase is reviewed.
- [ ] **17.2** Build the full solution in the same configuration used by generated media:
  - [ ] `Foundry`.
  - [ ] `Foundry.Core`.
  - [ ] `Foundry.Connect`.
  - [ ] `Foundry.Deploy`.
  - [ ] All available test projects.
- [ ] **17.3** Generate baseline media from WinUI `Foundry`:
  - [ ] ISO media with standard configuration and no optional expert features.
  - [ ] USB media on a disposable drive with standard configuration when hardware is available.
  - [ ] Confirm generated ISO/USB media matches the documented `X:\Foundry`, `Runtime`, `Config`, `Seed`, `Tools`, `OperatingSystem`, and `DriverPack` layout.
  - [ ] Confirm `Runtime\Foundry.Connect\<rid>` and `Runtime\Foundry.Deploy\<rid>` are present in the expected ISO and USB locations.
- [x] **17.4** Generate complete expert media from WinUI `Foundry`:
  - [x] Deploy localization configured.
  - [x] Connect provisioning enabled.
  - [x] Network provisioning enabled.
  - [x] Encrypted media secrets enabled when required.
  - [x] WinRE Wi-Fi boot image selected when Wi-Fi provisioning is enabled.
  - [x] Autopilot enabled with an embedded default test profile.
  - [x] Customization enabled when test values are available.
  - [x] ISO media generated successfully.
  - [x] USB media generated successfully on a disposable drive when hardware is available.
- [x] **17.5** Validate generated boot image contents before boot:
  - [x] `boot.wim` contains `X:\Foundry` runtime, configuration, bootstrap, secrets, network assets, Autopilot assets, and tools expected for the selected workflow.
  - [x] `boot.wim` does not contain plaintext Wi-Fi passphrases, plaintext media secret keys, or host-only workspace artifacts.
  - [x] WinRE Wi-Fi media contains the expected WinRE-derived boot image and injected drivers/assets.
  - [x] Temporary WinPE workspaces under `C:\ProgramData\Foundry\Workspaces\WinPe` are cleaned after successful and failed runs.
- [x] **17.6** Boot generated ISO media in a VM:
  - [x] WinPE reaches Foundry bootstrap.
  - [x] `Foundry.Connect` starts automatically or through the expected bootstrap handoff.
  - [x] `Foundry.Connect` loads generated Connect configuration from `X:\Foundry\Config`.
  - [x] `Foundry.Connect` reads generated network assets when present.
  - [x] `Foundry.Connect` decrypts embedded `aes-gcm-v1` secrets without prompting for a decryption key.
  - [x] `Foundry.Connect` completes or reaches the expected operator state without schema or path errors.
- [x] **17.7** Boot generated USB media:
  - [x] Physical USB boot reaches Foundry bootstrap when hardware is available.
  - [x] USB BOOT partition remains minimal and bootable.
  - [x] USB cache partition keeps persistent runtime/cache data under the documented layout.
  - [x] `Foundry.Connect` resolves runtime/cache paths correctly from USB media.
  - [x] Logs stay under expected WinPE and deployment locations; USB cache does not become the boot log sink.
- [x] **17.8** Validate `Foundry.Deploy` handoff:
  - [x] `Foundry.Deploy` can be launched from the `Foundry.Connect` runtime handoff.
  - [x] `Foundry.Deploy` loads generated `foundry.deploy.config.json`.
  - [x] Deploy localization values are loaded and applied.
  - [x] Deploy localization warnings are not emitted against the temporary empty catalog scope during startup.
  - [x] Deployment storage, OS image, driver, and customization settings load without relying on missing-root defaults.
  - [x] Runtime errors are logged with enough context to troubleshoot media layout or schema issues.
- [x] **17.9** Validate Autopilot generated-media behavior:
  - [x] Generated media embeds selected Autopilot profiles under `Foundry\Config\Autopilot\<FolderName>\AutopilotConfigurationFile.json`.
  - [x] Generated Deploy config stores the selected/default Autopilot profile folder name, not a full path.
  - [x] `Foundry.Deploy` reads the embedded default Autopilot profile when Autopilot is enabled and a profile is embedded.
  - [x] Invalid Autopilot-enabled media without an embedded profile is blocked by the `Start` page before ISO/USB creation, so the old Deploy recovery path is intentionally not reachable.
- [x] **17.10** Validate negative command gates from WinUI `Start`:
  - [x] ISO/USB creation is blocked when required Connect configuration is incomplete.
  - [x] ISO/USB creation is blocked when required Deploy configuration is incomplete.
  - [x] ISO/USB creation is blocked when encrypted secret-key provisioning is required but unavailable.
  - [x] ISO/USB creation is blocked or warned when Autopilot is enabled without a valid embedded profile.
- [x] **17.11** Validate operation logs:
  - [x] `C:\ProgramData\Foundry\Logs\Foundry.log` includes final ISO/USB start, progress, completion, and failure events.
  - [x] Logs include WinPE tool resolution, workspace build, provisioning payload generation, preparation stages, image customization progress, ISO/USB service completion, workspace cleanup, and runtime handoff context.
  - [x] Logs do not expose plaintext Wi-Fi passphrases, media secret keys, or decrypted secret values.
  - [x] `Foundry.Connect` and `Foundry.Deploy` runtime logs are written to expected WinPE/deployment locations.
  - [x] Pre-format cancellation is not part of the Phase 17 closeout path because destructive USB pre-format cancellation was not reproducible in the validated ISO/USB smoke matrix.
- [x] **17.12** Commit compatibility or targeted shared-logic fixes only if required:

```powershell
git commit -m "fix: preserve winpe runtime compatibility"
```

**Validation**

- [x] **17.13** ISO end-to-end smoke test completed in a VM.
- [x] **17.14** Physical USB end-to-end smoke test completed when hardware is available.
- [x] **17.15** Complete expert media smoke test completed through `Foundry.Connect` and `Foundry.Deploy`.
- [x] **17.16** No schema-breaking changes found.
  - [x] Complete effective Deploy config remains accepted by `Foundry.Deploy`.
  - [x] Complete effective Connect config remains accepted by `Foundry.Connect`.
  - [x] Generated media does not rely on sparse missing-root defaults.
- [x] **17.17** Deferred validations from Phases 12, 13, 14, 15, and 16 are closed or explicitly moved to a later cutover checklist item with a reason.

## Phase 18: Foundry UI/UX Review And Control Rationalization

**Priority:** high before cleanup, screenshots, documentation, and cutover.

**Goal:** make the migrated WinUI app feel like a coherent Foundry product instead of a functional prototype: define the Home page purpose, refine page layouts, verify control choices, enforce Microsoft WinUI 3/Fluent guidance for color, geometry, iconography, spacing, and typography, and replace DevWinUI page-level controls with native WinUI or Windows Community Toolkit equivalents where that improves clarity, supportability, or consistency.

**Role and scope:** Phase 18 must be approached as a UI/UX design and control-rationalization phase. Its role is to improve presentation, navigation clarity, page hierarchy, interaction affordances, responsive behavior, accessibility, and WinUI 3 control consistency. The business workflow, generated-media behavior, ADK detection/install flow, Connect/Deploy integration, and runtime configuration contracts are already in place and functional, as proven by the completed earlier migration phases and Phase 17 compatibility validation. Do not redesign business behavior in Phase 18 unless a UI review exposes a concrete integration bug that must be fixed to preserve the validated workflow.

**PR validation policy:** Implement Phase 18 in a dedicated branch/worktree and keep the pull request reviewable as separate commits while the UI is being validated. Do not squash the Phase 18 pull request until the implementation for the relevant slice has been reviewed and validated together.

**Prerequisites and boundary:** Run Phase 18 after Phase 17 compatibility validation. Phase 18 may change WinUI pages, layout, control usage, and shell/page presentation. It must not change generated media contracts, Connect/Deploy runtime configuration schemas, secret handling, or release packaging behavior unless a visual issue exposes a concrete integration bug. Keep DevWinUI as the shell/navigation baseline; do not blindly remove DevWinUI packages that still own navigation, title bar, breadcrumb, settings shell, or content-frame behavior.

**Audit baseline:** The Phase 18 audit was performed against the current WinUI pages and migration decisions. It found that the core product issue is not the shell, but page role clarity, action contracts, responsive layout, state feedback, and mixed page-level control ownership. DevWinUI remains the shell/navigation/title bar/breadcrumb baseline. Page content should use native WinUI 3 controls and Windows Community Toolkit controls as much as practical, with DevWinUI page-level controls retained only when they provide clear value or when replacement would create unnecessary risk. The current app has minimal resource-token coverage: `ThemeResources.xaml` mostly owns the Home cover image, `Styles.xaml` mostly owns page margins/padding, and `Fonts.xaml` is empty. Phase 18 must add a Foundry design-token pass before broad page redesign so page XAML follows WinUI 3 color, geometry, iconography, spacing, and typography guidance instead of accumulating page-local literals.

**Implementation order:** Execute Phase 18 as small UI slices, validating the plan after each slice:

1. Foundry WinUI design-token baseline: theme resources, typography, spacing, geometry, icon sizes, dialog sizing, and semantic status resources.
2. Home and ADK prerequisite clarity.
3. General and Start workflow handoff.
4. Network, Localization, Autopilot, and Customization expert-page cleanup.
5. Settings, About, Documentation, shell footer behavior, and operation progress surfaces.
6. Control-family cleanup and final manual visual validation.

Build after the design-token/package baseline and after each major implementation slice: Home/ADK, General/Start, expert pages, Settings/About/Documentation/shell cleanup, and final control-family cleanup.

**Validated UI decisions:**

- Decision: `Home` is a guided onboarding/status hub, not a dashboard and not a generic DevWinUI `AllLandingPage` catalog page.
- Decision: `Home` uses DevWinUI `MainLandingPage` as the landing shell because its `HeaderContent` and `FooterContent` slots match the desired guided-entry layout without rebuilding the whole page container.
- Decision: `Home` uses DevWinUI `HeaderTile` controls in `MainLandingPage.HeaderContent` for the four primary actions instead of generic `Card` controls.
- Decision: `Home` uses `MainLandingPage.FooterContent` for compact ADK readiness/status content and secondary supporting information, not for a dense dashboard summary.
- Decision: `Start` is the review-and-launch page; `General` edits generated media settings, including WinPE image/boot language.
- Decision: `Documentation` opens `https://foundry-osd.github.io/docs/intro` directly instead of navigating to an in-app documentation page.
- Decision: `About` owns product identity, version, update/release link, license, authors, support, and footer information, following the archived WPF Foundry About pattern and the simpler Connect/Deploy branded About layout.
- Decision: `About` opens as a modal `ContentDialog` hosting Foundry-owned About content, following the UniGetUI WinUI dialog-hosted page pattern at a simpler Foundry scope.
- Decision: `About` uses dialog sections/tabs for `About`, `Licenses`, `Contributors`, and `Release notes`; `Release notes` embeds the full GitHub releases list with WebView2.
- Decision: `About` uses WinUI `SelectorBar` for its dialog sections, following the UniGetUI compact dialog pattern.
- Decision: `About` uses a UniGetUI-style custom close button inside the dialog content with accessible name and keyboard support.
- Decision: `About` dialog uses a larger centered modal size than UniGetUI, with sane min/max constraints so the WebView2 release-notes tab has usable space.
- Decision: `Release notes` WebView2 navigation follows UniGetUI: load the releases URL in the embedded view, show progress, and do not intercept external navigation in Phase 18.
- Decision: `Licenses` uses a curated native WinUI list for Phase 18, not generated package-license automation.
- Decision: `Contributors` follows the UniGetUI pattern: curated contributor handles rendered with GitHub profile links and avatar URLs.
- Decision: Do not add a separate `Translators` tab in Phase 18; include translation credits under `Contributors` only if real credits exist.
- Decision: `About` is opened only from the shell/footer About command; do not add a duplicate Settings link or page.
- Decision: `Documentation` remains an external command only; the shell/footer command and the Home `Open documentation` card both open `https://foundry-osd.github.io/docs/intro` without creating an in-app documentation page, and the About dialog does not duplicate the documentation link.
- Decision: `About` tab links are limited to repository, issues/support, and license; release notes are accessed through the dedicated dialog tab.
- Decision: `Settings` owns app preferences and update management.
- Decision: `Settings` does not duplicate help/about/documentation surfaces; it owns app preferences, theme/backdrop, updates, diagnostics preferences, and developer-mode style options only.
- Decision: `Settings` uses the DevWinUI built-in settings slot with explicit selected-item mapping for Settings and settings subpages.
- Decision: `Home` keeps the `MainLandingPage` header area restrained and status-oriented; it must not become a marketing hero, a visual catalog, or a duplicated dashboard.
- Decision: `Home` action tiles use a responsive `HeaderContent` layout that wraps, scrolls horizontally, or otherwise remains usable at narrow, medium, and wide window sizes; do not use a raw non-wrapping horizontal `StackPanel` unless validation proves it behaves correctly.
- Decision: `HeaderTile.Link` is used only for true external links such as documentation. In-app workflow tiles navigate through Foundry navigation handlers/services so shell selection and page state remain correct.
- Decision: `ADK` install action is a combined Windows ADK + WinPE add-on flow; the UI must not present them as unrelated installers.
- Decision: `ADK` primary setup button keeps a stable combined label such as `Install ADK and WinPE add-on`, even when only one component is missing.
- Decision: `ADK` shows core readiness details by default and keeps diagnostics/logs collapsed by default.
- Decision: `General` action is renamed from `Create media` to a navigation-oriented label such as `Review and start`.
- Decision: Phase 18 removes UI code made obsolete by the redesign, including stale pages, view models, navigation metadata, resources, and localization keys.
- Decision: Phase 18 updates both `en-US` and `fr-FR` localization resources whenever labels, descriptions, actions, or page text change.
- Decision: Exact UI wording can be finalized during implementation unless a label is explicitly fixed in this plan; every finalized string must update `en-US` and `fr-FR` together.
- Decision: WinPE image/boot language remains on `General`; `Localization` owns deployment OS language behavior only.
- Decision: `Localization` keeps current deployed Windows language options and only cleans labels, order, layout, and validation, except for removing the redundant single-visible-language toggle.
- Decision: Keep operation progress in modal `ContentDialog` surfaces for Phase 18, and improve consistency, final success/failure states, sizing, and navigation blocking instead of replacing them with a shell overlay.
- Decision: Operation progress dialogs use a consistent title, progress display, final success/failure state, blocked-close behavior while running, and close/focus behavior after completion.
- Decision: Remove `Force single visible deployment language` from the Localization UI; derive single-language behavior from the visible deployment language selection while keeping the underlying config field internal if compatibility requires it.
- Decision: `Network` shows local field-level and section-level validation for Wi-Fi, 802.1X, certificates, and secrets, while final global readiness remains on `Start`.
- Decision: `Network` validation errors appear near the relevant field or section, not only in a global summary.
- Decision: `Autopilot` profile removal requires a confirmation `ContentDialog` because no undo model exists.
- Decision: `Autopilot` profile removal dialog includes the profile name and uses clear destructive-action wording.
- Decision: `Customization` is structurally retained; Phase 18 only improves labels, validation placement, spacing, and design-token alignment.
- Decision: Add the Windows Community Toolkit WinUI controls package during Phase 18 and migrate page-level settings-style cards/expanders to WCT where practical.
- Decision: Phase 18 follows Microsoft WinUI 3/Fluent guidance for color, geometry, iconography, spacing, and typography across the whole app.
- Decision: Control sizing and widths follow WinUI 3/Fluent recommendations and existing platform control behavior; avoid exotic custom dimensions, oversized controls, and page-local width hacks unless a specific responsive layout issue requires a documented exception.
- Decision: Use system WinUI typography and Segoe UI Variable; do not introduce a branded typeface in Phase 18.
- Decision: Status colors stay WinUI semantic/theme-resource based, not custom product-branded colors, so light, dark, and high-contrast behavior remains reliable.
- Decision: Use `AccentButtonStyle` only for the primary/relevant action in a section or dialog; secondary actions stay standard/subtle.
- Decision: Bitmap assets are reserved for app/product identity; page-level icons should use a consistent theme-safe vector icon approach.
- Decision: The Phase 18.1 design-token baseline is implemented as shared Foundry resources for spacing, typography aliases, icon sizes, input/control dimensions, dialog bounds, semantic status brushes, surface brushes, and high-contrast fallbacks. Remaining page redesign slices must consume these resources instead of adding new page-local literals.

- [x] **18.1** Establish the Foundry WinUI design-token baseline:
  - [x] Add the Windows Community Toolkit WinUI controls package during the first design-token/control baseline slice and verify the app builds before page-level migrations.
  - [x] Extend `ThemeResources.xaml`, `Styles.xaml`, and `Fonts.xaml` only where useful to define Foundry-owned semantic resources instead of page-local literals.
  - [x] Color uses WinUI `ThemeResource` brushes and system color resources; avoid hard-coded colors and use accent color sparingly for important actions, selection, and state.
  - [x] High contrast behavior uses compatible system foreground/background resources and does not rely on color alone to communicate readiness, warning, or blocked states.
  - [x] Geometry follows WinUI defaults: `ControlCornerRadius` for in-page controls, `OverlayCornerRadius` for dialogs/flyouts/overlays, and square corners only where adjacent edges join.
  - [x] Spacing follows a 4 epx measurement discipline with documented page, section, card, row, label/control, and dialog spacing tokens.
  - [x] Default sizing targets standard WinUI density; compact density is allowed only for clearly dense information surfaces after review.
  - [x] Control width, height, and minimum-size resources follow WinUI 3/Fluent defaults and recommendations; custom control dimensions must be conservative, reusable, and justified by content wrapping, localization, or responsive layout constraints.
  - [x] Typography uses the WinUI type ramp and built-in text styles such as title, subtitle/body strong, body, caption, and secondary text resources instead of ad hoc font sizes.
  - [x] Use Semibold only for hierarchy emphasis and avoid arbitrary bold/italic body styling.
  - [x] `AccentButtonStyle` is reserved for the primary/relevant action in a section or dialog, with standard/subtle buttons for secondary actions.
  - [x] Iconography standardizes on Segoe Fluent Icons or equivalent WinUI vector icons for commands, navigation, and status, using documented crisp icon sizes such as 16, 20, 24, and 32 epx.
  - [x] Define semantic status resources for `ready`, `warning`, `blocked`, `busy`, and `complete`, including brush, icon, text, and action-slot guidance.
  - [x] Define reusable dialog sizing resources for standard dialogs and the larger About/WebView2 dialog.
  - [x] Keep the final token inventory documentation tracked by Phase 18.16 so the PR records color, typography, spacing, geometry, icon sizes, status resources, and dialog sizing in one place.
- [x] **18.2** Define the Home page role:
  - [x] Replace the current generic DevWinUI `AllLandingPage` usage with a Foundry-owned `dev:MainLandingPage` composition.
  - [x] Home is a simple welcome/onboarding/status hub, not a dense operational dashboard.
  - [x] Configure `MainLandingPage.HeaderText` and `MainLandingPage.HeaderSubtitleText` with concise Foundry product/onboarding copy.
  - [x] Keep the `MainLandingPage` header area restrained; do not introduce a custom marketing hero, oversized visual treatment, or duplicated dashboard content.
  - [x] Remove Home-only cover resources only when the final `MainLandingPage` implementation no longer uses them.
  - [x] Keep the page intentionally sparse with only a short welcome message, compact ADK readiness/status content, and a small set of primary action tiles.
  - [x] Place the primary Home actions inside `MainLandingPage.HeaderContent`.
  - [x] Implement the four Home action tiles with DevWinUI `HeaderTile`: `Open ADK`, `Configure media`, `Review and start`, and `Open documentation`.
  - [x] Use clear title, short description, and theme-safe icon/source content for each `HeaderTile`; keep localized text short enough for compact tile layouts.
  - [x] Make the `HeaderTile` layout responsive across narrow, medium, and wide windows by wrapping, horizontal scrolling, or an equivalent validated layout behavior.
  - [x] Use `HeaderTile.Link` only for the external documentation tile; in-app tiles must route through Foundry navigation handlers/services.
  - [x] Keep Home action tiles clickable when prerequisites are missing; explain blockers in the ADK readiness/status content instead of disabling navigation.
  - [x] Place compact ADK readiness/status content in `MainLandingPage.FooterContent`, including whether Windows ADK and the WinPE add-on are both ready.
  - [x] The ADK readiness/status content shows only the essential state, using a clear success/non-success visual treatment such as ready, warning, or blocked.
  - [x] Do not show detailed update state, full media readiness, expert readiness, logs, or multi-section summaries on Home.
  - [x] Avoid duplicating every Expert page; Home should summarize and route, not become a second configuration surface.
  - [x] Use native WinUI layout and controls inside `HeaderContent` and `FooterContent` except for the approved DevWinUI `HeaderTile` action tiles.
- [ ] **18.3** Review page information architecture:
  - [ ] `General` owns generated media settings, including WinPE image/boot language.
  - [ ] Rename or reframe the current `Create media` action on `General` to a navigation-oriented label such as `Review and start`.
  - [ ] `Start` owns final media summary, USB selection, and ISO/USB execution.
  - [ ] `Start` presents readiness as scannable grouped checks with explicit blockers instead of a long prose summary.
  - [x] `ADK` answers one primary question first: whether Foundry can proceed, and what action the user should take next.
  - [ ] `Network`, `Localization`, `Autopilot`, and `Customization` remain expert workflow pages.
  - [x] Footer surfaces remain documentation/about/settings entry points, not workflow pages.
  - [x] `Documentation` is a direct external navigation action to `https://foundry-osd.github.io/docs/intro`, not a separate in-app page; when launched from Home or the shell/footer, it does not change the current in-app navigation selection.
  - [x] Show a fallback `ContentDialog` with selectable/copyable documentation URL text if the external documentation launch fails.
  - [x] Remove `DocumentationPage` from the WinUI app once Documentation becomes an external navigation action.
  - [ ] Replace shell-navigated About surfaces with one modal `ContentDialog`.
  - [ ] Implement the About dialog with UniGetUI-style `SelectorBar` sections: `About`, `Licenses`, `Contributors`, and `Release notes`.
  - [ ] Implement a custom content-level close button with accessible name, tooltip, and simple default WinUI tab order.
  - [ ] Size the About dialog larger than UniGetUI with responsive min/max constraints for 1366x768 and 1920x1080.
  - [ ] Open the About dialog on the `About` section every time.
  - [ ] Limit About-tab links to repository, issues/support, and license.
  - [ ] `About` tab uses a branded layout with app logo, app name, version, approved link set, authors, support, and footer text.
  - [ ] Render `Licenses` as a curated native WinUI list for Foundry and third-party licenses with external links.
  - [ ] Render `Contributors` as a curated native WinUI list with GitHub profile links and avatar URLs.
  - [ ] `Contributors` tab includes loading, broken-avatar, and offline fallback behavior so network failures do not break the dialog.
  - [ ] Use initials or a generic person icon when contributor avatars fail or the app is offline.
  - [ ] Do not add a separate `Translators` tab unless real Foundry translator credits exist.
  - [ ] Embed the full repository GitHub releases list with WebView2 at `https://github.com/foundry-osd/foundry/releases`.
  - [ ] Show release-notes loading progress, dispose WebView2 on close, and avoid intercepting external navigation in Phase 18.
  - [ ] WebView2 usage is scoped to release notes, includes loading/error/fallback UI, and documents runtime distribution expectations for the Velopack/unpackaged app.
  - [ ] Show a native fallback message with an `Open releases in browser` action if the GitHub releases page cannot load.
  - [ ] Keep Settings scoped to app preferences, theme/backdrop, updates, diagnostics preferences, and developer-mode style options.
  - [ ] Remove the duplicate `Settings > About` subpage and keep only the shell/footer About command.
  - [ ] Operation progress `ContentDialog` surfaces use one consistent structure for title, current operation text, progress indicator, optional log/details affordance, final success/failure state, and close behavior.
  - [ ] Operation progress dialogs block accidental close while work is running and restore a sensible focus target when complete.
- [ ] **18.4** Audit layout quality on every page:
  - [ ] No overlapping text, toggles, buttons, tables, or content dialogs.
  - [ ] Page command buttons are placed consistently and are visually tied to their section.
  - [ ] Settings sections use stable widths, spacing, and responsive constraints at common desktop sizes.
  - [ ] Replace fixed-width horizontal path/action rows with standard WinUI responsive layouts that wrap or stack cleanly at 1366x768 and with French text.
  - [ ] Replace repeated page-local margins, padding, `StackPanel` spacing, and fixed widths with approved spacing/control-width tokens based on WinUI 3/Fluent guidance.
  - [ ] Content dialogs size to their content within sane min/max constraints and remain centered.
  - [ ] Long paths, localized strings, and profile names are clipped or wrapped intentionally.
  - [ ] Avoid nested scroll friction where `ListView` or `TableView` sits inside an outer `ScrollView`.
- [ ] **18.5** Rationalize control ownership:
  - [ ] Prefer native WinUI 3 controls for common primitives: `Button`, `ToggleSwitch`, `ComboBox`, `InfoBar`, `ContentDialog`, `NavigationView`, `ListView`, layout panels, and command surfaces.
  - [ ] Prefer Windows Community Toolkit controls for Windows-settings-style page content: `SettingsCard` and `SettingsExpander` from the WinUI 3 toolkit package.
  - [ ] Use table/grid controls only where users need row scanning, selection, sorting, or multi-column comparison.
  - [ ] Use WebView2 only for the About dialog release-notes tab; keep other About tabs native WinUI to avoid web UI where static local content is sufficient.
  - [ ] Page-level controls must use the approved design-token baseline for color, typography, icon size, spacing, and corner radius.
  - [ ] Keep DevWinUI `MainLandingPage` and `HeaderTile` as approved Home-only exceptions where they provide the landing shell and primary action tiles.
  - [ ] Do not mix DevWinUI settings controls and Windows Community Toolkit settings controls on the same page unless there is a documented reason.
  - [ ] Migrate page-level `dev:SettingsCard` and `dev:SettingsExpander` usages page by page with the relevant UX slice instead of doing one broad control rewrite.
  - [ ] Keep DevWinUI shell controls where they provide the navigation/title bar/content-frame baseline.
  - [ ] Keep native `PasswordBox` handling for Wi-Fi and network secrets.
  - [ ] Keep `WinUI.TableView` on the main Autopilot profile list.
  - [ ] Replace the Autopilot tenant-selection dialog `TableView` with a clearer multi-select list pattern.
- [ ] **18.6** Audit DevWinUI usage:
  - [ ] Inventory DevWinUI controls used in Foundry workflow pages.
  - [ ] Classify each usage as shell-owned, page-layout-owned, or obsolete prototype usage.
  - [ ] Keep shell-owned DevWinUI usages: `MainWindow` title bar/navigation/breadcrumb integration, navigation metadata, resource dictionaries, and shell services.
  - [ ] Replace page-owned `dev:SettingsCard` and `dev:SettingsExpander` usages with native WinUI composition or WCT settings controls on `ADK`, `General`, `Start`, `Network`, `Localization`, `Autopilot`, `Customization`, `Settings`, `About`, and settings subpages where practical.
  - [ ] Keep DevWinUI page controls only when no native WinUI or WCT replacement fits the interaction cleanly, or when replacement creates unnecessary behavioral risk.
- [ ] **18.7** Review each workflow page for expected desktop UX:
  - [x] `ADK` clearly separates overall readiness, installed version/policy, WinPE add-on state, media capability, and install/upgrade/refresh actions.
  - [x] Put one ADK readiness card first, then lower-priority details for installed version, required version policy, WinPE add-on status, ISO/USB capability, and diagnostics/logs.
  - [x] Present ADK setup as one combined action that covers both Windows ADK and the WinPE add-on.
  - [x] Keep the ADK primary setup button label stable, such as `Install ADK and WinPE add-on`, even when only one component is missing.
  - [x] Show installed version, required version policy, WinPE add-on state, and media capability without extra clicks; keep diagnostics/logs collapsed by default.
  - [x] `ADK` removes duplicate operation-status text and shows operation progress only when useful.
  - [ ] `General` makes generated media settings scannable without hiding required execution prerequisites.
  - [ ] `General` shows disabled/empty reasons for WinPE language discovery and other unavailable prerequisites.
  - [ ] `Start` clearly distinguishes readiness checks, USB target selection, and final commands.
  - [ ] Organize `Start` readiness into grouped checklist sections: prerequisites, media output, runtime payloads, and expert configuration.
  - [ ] `Start` readiness items show ready, warning, blocked, or not configured states with links to the owning page when action is needed.
  - [ ] Wire `Start` readiness links to navigate only to the owning page without cross-page auto-focus, auto-scroll, or forced section expansion.
  - [ ] `Home`, `ADK`, and `Start` share the same tokenized status-surface contract for ready, warning, blocked, busy, and complete states.
  - [ ] `Start` shows explicit USB loading, empty, and error states while discovery runs or fails.
  - [ ] Preserve current USB candidate selection behavior during the `Start` page cleanup.
  - [ ] `Network` uses stronger progressive disclosure for Ethernet 802.1X and Wi-Fi instead of opening all advanced sections by default.
  - [ ] Collapse Ethernet 802.1X and Wi-Fi sections by default unless the section is enabled, configured, or invalid.
  - [ ] Place Network validation feedback next to the field or section it blocks and keep secret handling opaque.
  - [ ] Keep WinPE image/boot language on `General`, not on `Localization`.
  - [ ] `Localization` clearly owns deployment OS language choices and removes the separate single-visible-language toggle from the user-facing UI.
  - [ ] Preserve current visible deployment language semantics while removing the user-facing `Force single visible deployment language` toggle.
  - [ ] Reset default deployment language to `Automatic` when it no longer belongs to the selected visible deployment language set.
  - [ ] `Autopilot` separates enablement, import/download actions, default profile, profile inventory, and destructive removal behavior.
  - [ ] `Autopilot` shows visible busy/status feedback for import and tenant download operations.
  - [ ] `Autopilot` replaces or repairs the tenant profile picker interaction model so multi-select is accessible and keyboard-clear.
  - [ ] Add Autopilot profile deletion confirmation with the profile name and destructive-action wording.
  - [ ] Keep both Customization machine naming options while improving labels, validation placement, spacing, and design-token alignment.
  - [ ] `Customization` clarifies machine naming labels/descriptions, for example `Auto-generate computer name suffix` and `Allow suffix edit during deployment`.
  - [ ] `Settings` uses the meta-page header pattern; `About` uses the branded tabbed dialog header; `Documentation` opens externally and has no in-app header.
  - [ ] `Settings` selected-item synchronization handles the built-in settings item and settings subpages correctly.
  - [ ] Remove the title-bar theme quick toggle.
  - [ ] Remove the title-bar search box.
- [ ] **18.8** Review accessibility and localization resilience:
  - [ ] Keyboard navigation reaches all interactive controls in a sensible order.
  - [ ] Icon-only actions have accessible names and tooltips.
  - [ ] Icon-only actions use approved vector icons and documented icon sizes; bitmap icons are allowed only for brand/product identity.
  - [ ] Disabled controls have visible reasons nearby when the reason is not obvious.
  - [ ] Runtime language switching does not leave stale text on reviewed pages.
  - [ ] French and English text fit without overlapping at common desktop sizes.
  - [ ] All changed UI strings are updated in both `en-US` and `fr-FR` resources in the same Phase 18 slice.
  - [ ] Exact UI labels/descriptions may be finalized during implementation unless this plan fixes the label explicitly.
  - [ ] Remove obsolete localization keys for deleted pages, removed actions, and retired controls once no code references remain.
  - [ ] Localized requirement text keeps the same operational meaning in English and French, especially ADK version policy text.
  - [ ] Update date/time text follows the active UI culture instead of hard-coded invariant formatting.
  - [ ] Settings no-op guard handling is either removed or made meaningful through shell-owned guard state.
- [ ] **18.9** Remove obsolete UI artifacts:
  - [ ] Remove pages, view models, navigation entries, commands, resources, assets, and localization keys that become unused after the Phase 18 UI redesign.
  - [ ] Remove obsolete artifacts in the same implementation slice that makes them obsolete, then run one final stale-reference sweep before Phase 18 completion.
  - [ ] Confirm removed `DocumentationPage`, duplicate About surfaces, title-bar search/theme controls, and retired DevWinUI page-level controls leave no stale references.
- [ ] **18.10** Commit:

```powershell
git commit -m "refactor(ui): refine foundry winui experience"
```

**Validation**

- [ ] **18.11** Manual UI smoke test completed at 1366x768 and 1920x1080.
- [ ] **18.12** Manual UI smoke test completed in light and dark themes.
- [ ] **18.13** Manual UI smoke test completed in at least one Windows contrast theme for tokenized status surfaces and dialogs.
- [ ] **18.14** Manual runtime language-switch smoke test completed for reviewed pages.
- [ ] **18.15** No page has overlapping controls, truncated primary actions, or unusable dialogs.
- [ ] **18.16** Design-token inventory is documented in the PR description: color, typography, spacing, geometry, icon sizes, status resources, and dialog sizing.
- [ ] **18.17** DevWinUI usage review is documented in the PR description: kept shell usages, replaced page-level usages, and intentionally retained page-level usages.
- [ ] **18.18** Final implementation validation includes screenshot pairs or documented visual checks for Home, ADK, Start, Network, and About at 1366x768 and 1920x1080 in light and dark themes; this is not required during the audit-only planning pass.
- [ ] **18.19** No new WinUI or WPF UI tests are added; visual/layout behavior remains manually validated.

## Phase 19: Cleanup And Dependency Review

**Priority:** medium.

**Goal:** remove prototype leftovers and reduce long-term maintenance risk.

**Boundary:** Phase 19 removes only confirmed obsolete migration/prototype leftovers. Do not remove the archived WPF reference before the first stable WinUI release has been validated, because it remains the behavior reference for compatibility investigations. Do not remove DevWinUI shell assets, `AppData.json` navigation metadata, or DevWinUI packages that remain part of the long-term shell baseline.

- [ ] **19.1** Remove DevWinUI placeholder strings and metadata.
- [ ] **19.2** Remove unused settings pages or rename them to product-specific pages.
- [ ] **19.3** Remove unused packages.
- [ ] **19.4** Keep DevWinUI packages as the long-term shell baseline unless a specific package becomes unused after Foundry pages are integrated.
- [ ] **19.5** Review trimming settings:
  - [ ] Disable trimming if it breaks reflection-heavy dependencies.
  - [ ] Add annotations only where needed.
- [ ] **19.6** Remove x86 prototype leftovers:
  - [ ] Remove `x86` from WinUI app `Platforms` only if it still exists.
  - [ ] Remove `win-x86` from WinUI app `RuntimeIdentifiers` only if it still exists.
  - [ ] Confirm no x86 publish profile, workflow matrix entry, installer, or release artifact remains.
  - [ ] Do not remove legitimate `x86` references for Windows Kits paths, ADK tooling, driver metadata, third-party asset documentation, or WPF runtime support.
- [ ] **19.7** Review unpackaged app leftovers from the WinUI template:
  - [ ] Keep `Package.appxmanifest` only if the WinUI build or Visual Studio tooling still requires it; otherwise remove it.
  - [ ] Remove stale packaged-only context menu declarations only if Velopack/unpackaged install path does not use them.
  - [ ] Remove or replace `RuntimeHelper.IsPackaged()` branches only when they are unreachable or contradict the selected unpackaged Velopack model.
  - [ ] Document any retained unpackaged-template artifact with the concrete reason it is still required.
- [ ] **19.8** Confirm Phase 11 removed `nucs.JsonSettings` from the WinUI app.
  - [ ] Confirm no runtime dependency still requires it.
  - [ ] Confirm persisted app settings use the internal settings service.
- [ ] **19.8.1** Confirm Phase 11 removed or replaced remaining DevWinUI prototype AppData working directory usage.
- [ ] **19.9** Commit:

```powershell
git commit -m "chore: clean up winui migration leftovers"
```

**Validation**

- [ ] **19.10** `dotnet list .\src\Foundry\Foundry.csproj package`.
- [ ] **19.11** Confirm no placeholder DevWinUI URLs remain.
- [ ] **19.12** Confirm no `bin`, `obj`, `.vs`, or `.csproj.user` files are tracked.

## Phase 20: Documentation Update

**Priority:** medium-low.

**Goal:** make repository and user documentation match the new app.

**Boundary:** Repository docs live in this repo. User-facing documentation site updates may need the adjacent `foundry-osd.github.io` repository and should be handled as a separate docs-site change when release links, screenshots, or workflow pages need to change. Do not update user-facing workflow screenshots before Phase 16.E, Phase 17 smoke validation, and Phase 18 UI review are complete.

- [ ] **20.1** Update `README.md`.
- [ ] **20.2** Update developer build docs.
- [ ] **20.3** Update release process docs.
- [ ] **20.4** Update installation/update docs.
- [ ] **20.5** Update screenshots only after UI stabilizes.
- [ ] **20.6** Update docs site if needed:
  - [ ] Confirm the exact adjacent repository path before editing.
  - [ ] Download links point to `FoundrySetup-x64.msi` and `FoundrySetup-arm64.msi`.
  - [ ] Foundry standard workflow.
  - [ ] Media creation.
  - [ ] Expert mode.
  - [ ] Configuration localization.
- [ ] **20.7** Commit:

```powershell
git commit -m "docs: document winui foundry migration"
```

**Validation**

- [ ] **20.8** Build docs site if changed.
- [ ] **20.9** Confirm install/update instructions match Velopack artifacts.

## Phase 21: Final Cutover To Main

**Priority:** final.

**Goal:** merge the migration safely and restore production automation.

- [ ] **21.1** Ensure all PRs are merged into `feat/winui-migration`.
- [ ] **21.2** Run full CI on `feat/winui-migration`.
- [ ] **21.3** Run manual release dry run or draft release.
- [ ] **21.4** Verify release artifacts:
  - [ ] Foundry Velopack packages.
  - [ ] Foundry MSI installers.
  - [ ] Foundry.Connect ZIPs.
  - [ ] Foundry.Deploy ZIPs.
- [ ] **21.5** Verify installed app startup on a clean machine or clean VM:
  - [ ] Windows App SDK runtime/bootstrap initialization succeeds before WinUI APIs are used.
  - [ ] Missing or present Windows App SDK runtime state is handled by the selected Velopack MSI distribution model.
  - [ ] Installed app launches without requiring Visual Studio or a development environment.
- [ ] **21.6** Commit release workflow restoration after a successful manual release dry run:

```powershell
git commit -m "ci: restore scheduled releases after winui migration"
```

- [ ] **21.7** Merge `feat/winui-migration` into `main`.
- [ ] **21.8** Tag first WinUI release.
  - [ ] Use date-based tag format `vYY.M.D.Build`.
- [ ] **21.9** Re-enable Sunday scheduled release only after the first manual WinUI release succeeds from `main`.
- [ ] **21.10** Monitor first release installation/update telemetry manually through GitHub issues/downloads/log reports.
- [ ] **21.11** Keep the WPF reference archive until the first stable WinUI release has been validated.
- [ ] **21.12** After merge validation, delete merged feature branches and clean up migration worktrees.

**Validation**

- [ ] **21.13** CI passes on `main`.
- [ ] **21.14** Release workflow succeeds on manual dispatch.
- [ ] **21.15** Scheduled release is enabled again.

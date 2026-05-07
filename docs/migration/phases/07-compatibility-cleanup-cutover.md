# Compatibility, UI Review, Cleanup, And Cutover Phases

## Phase 17: Foundry.Connect And Foundry.Deploy Compatibility Pass

**Priority:** high before cutover.

**Goal:** prove the migrated WinUI app still produces runtime artifacts consumed by WPF Connect/Deploy.

**Prerequisites and boundary:** Run Phase 17 after Phase 16.E final media command enablement is complete. Phase 17 should prove compatibility; only commit targeted fixes when the smoke tests expose a concrete incompatibility.

- [ ] **17.1** Build `Foundry.Connect` as WPF.
- [ ] **17.2** Build `Foundry.Deploy` as WPF.
- [ ] **17.3** Generate WinPE media from WinUI `Foundry`.
  - [ ] ISO media.
  - [ ] USB media on a disposable drive when hardware is available.
  - [ ] Standard workflow without optional Network/Autopilot settings.
  - [ ] Expert workflow with deploy localization and generated Connect configuration.
  - [ ] Expert workflow with Autopilot enabled when a test profile is available.
- [ ] **17.4** Boot test media in a VM.
- [ ] **17.5** Confirm `Foundry.Connect` starts in WinPE.
- [ ] **17.6** Confirm `Foundry.Connect` reads generated network configuration.
- [ ] **17.7** Confirm `Foundry.Deploy` can be launched from the runtime handoff.
- [ ] **17.8** Confirm deployment configuration loads.
- [ ] **17.8.1** Confirm `Foundry.Deploy` reads generated Autopilot configuration when Autopilot is enabled and a profile is embedded.
- [ ] **17.8.2** Confirm `Foundry.Deploy` keeps Autopilot operator controls visible when Autopilot is enabled but no profile is embedded.
- [ ] **17.9** Confirm both runtime agents use the normalized layout:
  - [ ] `Runtime\Foundry.Connect\<rid>`.
  - [ ] `Runtime\Foundry.Deploy\<rid>`.
- [ ] **17.10** Confirm logs are written to expected locations.
- [ ] **17.11** Commit compatibility or targeted shared-logic fixes only if required:

```powershell
git commit -m "fix: preserve winpe runtime compatibility"
```

**Validation**

- [ ] **17.12** VM smoke test completed.
- [ ] **17.13** Physical USB smoke test completed if hardware is available.
- [ ] **17.14** No schema-breaking changes found.
  - [ ] Complete effective Deploy config remains accepted by `Foundry.Deploy`.
  - [ ] Complete effective Connect config remains accepted by `Foundry.Connect`.
  - [ ] Generated media does not rely on sparse missing-root defaults.

## Phase 18: Foundry UI/UX Review And Control Rationalization

**Priority:** high before cleanup, screenshots, documentation, and cutover.

**Goal:** make the migrated WinUI app feel like a coherent Foundry product instead of a functional prototype: define the Home page purpose, refine page layouts, verify control choices, and replace DevWinUI page-level controls with native WinUI or Windows Community Toolkit equivalents where that improves clarity, supportability, or consistency.

**Prerequisites and boundary:** Run Phase 18 after Phase 17 compatibility validation. Phase 18 may change WinUI pages, layout, control usage, and shell/page presentation. It must not change generated media contracts, Connect/Deploy runtime configuration schemas, secret handling, or release packaging behavior unless a visual issue exposes a concrete integration bug. Keep DevWinUI as the shell/navigation baseline; do not blindly remove DevWinUI packages that still own navigation, title bar, breadcrumb, settings shell, or content-frame behavior.

- [ ] **18.1** Define the Home page role:
  - [ ] Home is a simple welcome/onboarding page, not a dense operational dashboard.
  - [ ] Keep the page intentionally sparse with only a short welcome message, a lightweight ADK status card, and a small set of primary action buttons.
  - [ ] The ADK status card shows only the essential state, using a clear success/non-success visual treatment such as green for compatible/ready and a warning/error state when missing or incompatible.
  - [ ] Provide direct next actions such as open ADK, configure media, open Start, or open documentation.
  - [ ] Do not show detailed update state, full media readiness, expert readiness, logs, or multi-section summaries on Home.
  - [ ] Avoid duplicating every Expert page; Home should summarize and route, not become a second configuration surface.
- [ ] **18.2** Review page information architecture:
  - [ ] `General` owns standard media defaults.
  - [ ] `Start` owns final media summary, USB selection, and ISO/USB execution.
  - [ ] `Network`, `Localization`, `Autopilot`, and `Customization` remain expert workflow pages.
  - [ ] Footer pages remain documentation/about/settings surfaces, not workflow pages.
- [ ] **18.3** Audit layout quality on every page:
  - [ ] No overlapping text, toggles, buttons, tables, or content dialogs.
  - [ ] Page command buttons are placed consistently and are visually tied to their section.
  - [ ] Settings sections use stable widths, spacing, and responsive constraints at common desktop sizes.
  - [ ] Content dialogs size to their content within sane min/max constraints and remain centered.
  - [ ] Long paths, localized strings, and profile names are clipped or wrapped intentionally.
- [ ] **18.4** Rationalize control ownership:
  - [ ] Prefer native WinUI 3 controls for common primitives: `Button`, `ToggleSwitch`, `ComboBox`, `InfoBar`, `ContentDialog`, `NavigationView`, `ListView`, layout panels, and command surfaces.
  - [ ] Prefer Windows Community Toolkit controls for Windows-settings-style page content when they fit: `SettingsCard` and `SettingsExpander` from the WinUI 3 toolkit package.
  - [ ] Use table/grid controls only where users need row scanning, selection, sorting, or multi-column comparison.
  - [ ] Do not mix DevWinUI settings controls and Windows Community Toolkit settings controls on the same page unless there is a documented reason.
  - [ ] Keep DevWinUI shell controls where they provide the navigation/title bar/content-frame baseline.
- [ ] **18.5** Audit DevWinUI usage:
  - [ ] Inventory DevWinUI controls used in Foundry workflow pages.
  - [ ] Classify each usage as shell-owned, page-layout-owned, or obsolete prototype usage.
  - [ ] Replace page-layout-owned DevWinUI controls when a native WinUI or Windows Community Toolkit equivalent is clearer and does not regress behavior.
  - [ ] Keep DevWinUI page controls only when no better native/toolkit equivalent exists or when replacement would add churn without product value.
- [ ] **18.6** Review each workflow page for expected desktop UX:
  - [ ] `ADK` clearly separates installed state, compatibility state, and install/repair actions.
  - [ ] `General` makes media defaults scannable without hiding required execution prerequisites.
  - [ ] `Start` clearly distinguishes readiness summary, USB target selection, and final commands.
  - [ ] `Network` keeps advanced 802.1X/Wi-Fi sections readable without excessive nesting.
  - [ ] `Localization` clearly separates WinPE boot language from deployment OS language choices.
  - [ ] `Autopilot` presents import, tenant download, default profile, and imported profile list as a coherent management surface.
  - [ ] `Customization` keeps machine naming controls compact and validation visible.
- [ ] **18.7** Review accessibility and localization resilience:
  - [ ] Keyboard navigation reaches all interactive controls in a sensible order.
  - [ ] Icon-only actions have accessible names or tooltips.
  - [ ] Disabled controls have visible reasons nearby when the reason is not obvious.
  - [ ] Runtime language switching does not leave stale text on reviewed pages.
  - [ ] French and English text fit without overlapping at common desktop sizes.
- [ ] **18.8** Commit:

```powershell
git commit -m "refactor(ui): refine foundry winui experience"
```

**Validation**

- [ ] **18.9** Manual UI smoke test completed at 1366x768 and 1920x1080.
- [ ] **18.10** Manual UI smoke test completed in light and dark themes.
- [ ] **18.11** Manual runtime language-switch smoke test completed for reviewed pages.
- [ ] **18.12** No page has overlapping controls, truncated primary actions, or unusable dialogs.
- [ ] **18.13** DevWinUI usage review is documented in the PR description: kept shell usages, replaced page-level usages, and intentionally retained page-level usages.
- [ ] **18.14** No new WinUI or WPF UI tests are added; visual/layout behavior remains manually validated.

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

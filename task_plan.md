# Task Plan: Foundry WinUI 3 Migration Study

## Goal
Prepare a code-informed migration study for moving only `src/Foundry` from WPF to WinUI 3 on .NET 10 while keeping `Foundry.Connect` and `Foundry.Deploy` as WPF projects.

## Current Phase
Phase 13: Implementation Readiness and Publish Validation Closure

## Constraints
- Plan phase only.
- No implementation.
- No code, project, solution, workflow, script, packaging, asset, or test changes.
- Planning artifacts may be written.
- Read-only subagents are allowed for inventory and impact analysis.
- Use current documentation for WinUI 3 and .NET guidance.

## Phases

### Phase 1: Repository Topology and Build Discovery
- [x] Inspect solution structure.
- [x] Inspect shared MSBuild configuration.
- [x] Validate application and test project target frameworks and WPF usage.
- [x] Record build topology findings.
- **Status:** complete

### Phase 2: Foundry Application Migration Surface
- [x] Inspect startup and application lifetime.
- [x] Inspect DI, host, services, shell, dialogs, theme handling, dispatching, resources, and XAML.
- [x] Identify direct and hidden WPF dependencies.
- [x] Record migration surface findings.
- **Status:** complete

### Phase 3: Architecture Quality Review
- [x] Assess MVVM boundaries.
- [x] Assess viewmodel purity and service boundaries.
- [x] Identify areas to keep, adapt, redesign, replace, or remove.
- **Status:** complete

### Phase 4: Cross-Project Impact Review
- [x] Inspect `Foundry.Connect`.
- [x] Inspect `Foundry.Deploy`.
- [x] Trace shared assets, build assumptions, publish assumptions, and runtime coupling.
- **Status:** complete

### Phase 5: Packaging, Publish, Release, and Workflow Review
- [x] Inspect GitHub workflows.
- [x] Inspect scripts and publish/release/archive logic.
- [x] Identify WinUI 3 operational differences for this repository.
- **Status:** complete

### Phase 6: Current Documentation Check
- [x] Use Context7 for current WinUI 3 and Windows App SDK guidance.
- [x] Use authoritative sources if Context7 is insufficient for publish or packaging behavior.
- [x] Record external documentation references.
- **Status:** complete

### Phase 7: Strategy, Risks, and Decision Matrix
- [x] Build keep/adapt/redesign/replace/remove matrix.
- [x] Build staged migration strategy.
- [x] List risks, blockers, unknowns, and validation decisions.
- **Status:** complete

### Phase 8: Final Plan Delivery
- [x] Produce final repository-specific analysis in the requested structure.
- [x] Confirm no implementation was performed.
- [x] Confirm only planning files were modified.
- **Status:** complete

### Phase 9: Decision Refinement - Velopack, Localization, and WinUI Shell
- [x] Record user validation for Foundry distribution model.
- [x] Verify Velopack MSI packaging guidance.
- [x] Reassess release workflow impact for Velopack.
- [x] Reassess WinUI shell direction with NavigationView.
- [x] Reassess `.resx` localization strategy for WinUI.
- [x] Clarify project-name and migration-scope expectations.
- **Status:** complete

### Phase 10: WinUI Shell and UX Specification
- [x] Lock NavigationView information architecture.
- [x] Lock removal of Standard/Expert modes.
- [x] Lock page ownership for Home, ADK, Configuration, Start, and expert pages.
- [x] Lock Start page summary and generation flow.
- [x] Lock blocking progress dialog behavior and cancellation semantics.
- [x] Lock Settings scope and update UX.
- [x] Record UI implementation implications and validation expectations.
- **Status:** complete

### Phase 11: Toolkit and UI Verification Policy
- [x] Record Windows Community Toolkit usage policy.
- [x] Record first Toolkit UI package target.
- [x] Record run-every-page UI verification requirement.
- [x] Record manual layout, theme, language, navigation, validation, and dialog checks.
- **Status:** complete

### Phase 12: Deep Plan Audit Closure
- [x] Re-audit planning artifacts against the actual repository.
- [x] Re-audit WinUI, Velopack, and Windows Community Toolkit documentation.
- [x] Record release topology, Velopack identity, MSI scope, architecture channel, and delta-update policy.
- [x] Record strict UI page ownership, language scopes, validation model, dialog/picker mapping, and update-surface decisions.
- [x] Remove stale "unknown packaging" and phase-status contradictions from the plan.
- **Status:** complete

### Phase 13: Implementation Readiness and Publish Validation Closure
- [x] Record second-audit closure decisions for ADK navigation gating, status surfaces, Logs, About, Autopilot picker, and configuration actions.
- [x] Record preservation of Foundry administrator manifest behavior.
- [x] Record Connect/Deploy runtime-layout asymmetry and local override validation requirements.
- [x] Record deep Foundry publish behavior validation before Velopack packaging.
- [x] Record Velopack input/output, install, update, uninstall, and failure-case validation.
- **Status:** complete

## Key Questions
1. Which WPF assumptions are global today, and what must change for a mixed WinUI 3 + WPF solution?
2. How much of `Foundry` is framework-agnostic MVVM/business logic versus WPF-specific UI infrastructure?
3. Which current abstractions should be retained versus redesigned for WinUI 3?
4. Does `Foundry` publish or output layout affect `Foundry.Connect`, `Foundry.Deploy`, scripts, workflows, or release artifacts?
5. Which WinUI 3 packaging mode is most appropriate for this repository?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Use the existing dedicated worktree `foundry-winui3-migration-study` on `codex/winui3-migration-study` | Satisfies the requirement to avoid the primary checkout and keep the study isolated from implementation branches. |
| Limit writes to `task_plan.md`, `findings.md`, and `progress.md` during plan phase | Matches the user's explicit permission for plan files while preserving hard-stop implementation constraints. |
| Foundry may stop shipping as `Foundry-x64.exe` / `Foundry-arm64.exe` single-file downloads | User validated moving away from single-file distribution. Velopack MSI is the target. |
| Foundry distribution target is unpackaged WinUI 3 packaged through Velopack-generated MSI | User rejected MSIX and selected MSI through Velopack. This changes Foundry release artifacts but not Connect/Deploy runtime archive contracts. |
| The WinUI shell should be redesigned around NavigationView and remove Standard/Expert mode switching | User wants a WinUI redesign, all pages visible, no mode toggle, and a page hierarchy with General and Expert sections. |
| Foundry should keep the project name `Foundry` | The migration is a conversion of the existing app project identity, not creation of a differently named replacement project. |
| Keep `.resx` as the pragmatic initial localization source unless a specific WinUI feature requires `.resw` | Current Foundry localization is service/viewmodel-driven, runtime-switchable, and used from non-UI services. `.resw` remains worth evaluating for XAML/manifest-specific WinUI localization. |
| ISO/USB generation should always use the full configuration model | Removing Standard/Expert mode means generation must no longer exclude expert configuration based on mode state. Default or empty page settings should produce default behavior. |
| Start page owns final review and creation actions | User wants a Summary/Start page with key configured values, readiness state, and Create ISO/Create USB buttons. |
| Operation progress should run in a locked ContentDialog | Navigation/settings changes must be blocked during provisioning. USB confirmation happens before opening the progress dialog. |
| ISO and USB cancellation should be best-effort safe stop | User wants Cancel for both operations, with cleanup where possible and clear terminal state reporting. |
| Settings should be a full NavigationView page | Settings first scope includes theme, language, update check, logs folder, cache/temp locations, and basic diagnostics. |
| Velopack updates should prompt on startup and restart only after explicit user confirmation | Startup check shows an update dialog when available. Manual check lives in Settings. Use `win-x64-stable` and `win-arm64-stable` channels. |
| Windows Community Toolkit should be used where it adds clear value | Native WinUI controls remain the default, but Toolkit controls are approved when they improve fit and reduce custom UI work. |
| Toolkit Settings controls are the first approved UI Toolkit target | Use `CommunityToolkit.WinUI.Controls.SettingsControls` for the Settings page, especially `SettingsCard` and `SettingsExpander`. |
| The implementation should run Foundry after every migrated page | UI layout and behavior must be verified continuously, not only at final smoke test. |
| Keep one public GitHub release per Foundry version | Each release must include Foundry Velopack desktop assets plus unchanged Connect/Deploy WinPE zip artifacts so existing latest-release bootstrap behavior remains valid. |
| Publish GitHub releases only after all assets are built and validated | The current release-before-assets workflow can create a broken latest release; implementation must build/package/validate first, then publish last. |
| Use `FoundryOSD.Foundry` as the Velopack `packId` | This is more unique and stable than `Foundry`, while keeping `Foundry` as the executable and user-facing product title. |
| Use Velopack MSI `PerMachine` install scope | Foundry requires administrator privileges and uses shared ProgramData workspaces, so per-machine installation is the safest default. |
| Publish Foundry as self-contained, unpackaged, non-single-file output | User selected self-contained distribution; Windows App SDK/WinUI unpackaged output must not be treated as a single-file executable. |
| Use architecture-specific Velopack stable channels | Use `win-x64-stable` and `win-arm64-stable` so x64 and ARM64 update feeds do not collide. |
| Allow Velopack deltas only after proof | Delta packages may be used only if implementation proves GitHub Releases plus multi-architecture channels behave correctly; otherwise disable deltas for the first rollout. |
| Treat code signing as out of scope for now | Do not block migration or MSI planning on signing; retain it only as a later release-hardening consideration. |
| Lock strict WinUI page ownership | `Home` is read-only status, `Configuration` owns editable media/general inputs, and `Start` owns final review plus ISO/USB actions. |
| Define language scopes explicitly | `App language` belongs in Settings, `WinPE language` belongs in Configuration, and deployment language/time-zone settings belong in Localization. |
| App language changes should live-refresh pages and navigation | Open modal dialogs may refresh on reopen, but normal shell/page text should update without restart. |
| Use a central readiness issue model | Pages contribute blockers and warnings with owning-page targets; Start aggregates them with navigation links. |
| Map app dialogs to ContentDialog and OS pickers | App-owned dialogs become WinUI `ContentDialog`; file/folder pickers remain OS pickers initialized with the active window handle. |
| Manual update actions live only in Settings | About shows product/version information only; non-Velopack local builds disable update install actions gracefully. |
| Importing configuration navigates to Start | After import, the user should see readiness, warnings, and next actions immediately. |
| Remove the persistent global footer/status surface | Show readiness, USB count, version, and operation state on Home/Start or modal operation dialogs instead of a shell footer. |
| Preserve Foundry administrator execution | The WinUI project conversion must keep `app.manifest` and `requireAdministrator` because ADK, WinPE, and USB operations require elevation. |
| Gate non-ADK pages until ADK is compatible | Until compatible ADK/WinPE Add-on is detected, only Home, ADK, Settings, and About are accessible; locked pages stay visible but disabled. |
| Put each operation status on its owning page | ADK progress lives on ADK, Autopilot progress on Autopilot, import/export results on Start InfoBars, and ISO/USB progress in the locked operation dialog. |
| Move Logs to Settings | Logs is not a NavigationView footer item; it is a Settings read-only card/action that opens the logs folder. |
| Keep About as a footer ContentDialog | About remains a footer action with version, license, authors, support, and project links, but no update check. |
| Keep Autopilot tenant profile picker modal | Use a large scrolling ContentDialog preserving table selection, multi-select, Ctrl+A, and import footer actions. |
| Start exposes only the existing configuration actions | Move import expert config, export expert config, and export deploy config to Start; do not add deploy-config import. |
| Export deploy config from full defaults | After Standard/Expert mode removal, export deploy config from the full current configuration even when values are at safe defaults. |
| Treat Settings paths as read-only first | Logs/cache/temp locations are shown as read-only cards with open-folder actions; do not add editable path preferences. |
| Deep-test Foundry publish behavior before packaging | Foundry publish output must be validated directly and repeatedly before it is used as Velopack input. |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Initial planning files were created in the primary checkout because `apply_patch` used the thread default directory | 1 | Removed only those plan artifacts from the primary checkout and recreated them in the dedicated worktree. |

## Notes
- Subagents must remain read-only.
- Any checkpoint commits in this phase must contain only planning artifacts.
- Implementation must wait for explicit user validation.

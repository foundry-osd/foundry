# Migration Workflow

## Recommended Branch And Worktree Strategy

- [ ] Keep `main` as the stable reference branch.
- [ ] Create one long-lived integration branch for the migration:
  - [ ] `feat/winui-migration`.
- [ ] Use short-lived feature branches or worktrees for each independent migration phase.
- [ ] Merge short-lived branches into `feat/winui-migration` through PRs.
- [ ] Run the full CI matrix for PRs targeting `feat/winui-migration`.
- [ ] Treat `Build (x64)` and `Build (ARM64)` as the gating CI checks for migration PR readiness.
- [ ] Ignore `submit-nuget` failures for all migration PR readiness decisions unless the maintainer explicitly asks to investigate that workflow.
- [ ] Merge `feat/winui-migration` into `main` only when the WinUI app can replace the WPF app without breaking CI/release.
- [ ] Prefer `git worktree` for parallel implementation when two tasks touch different paths.
- [ ] Use one worktree per independent slice:
  - [ ] `worktrees/foundry-core-extraction`.
  - [ ] `worktrees/foundry-winui-import`.
  - [ ] `worktrees/foundry-velopack`.
  - [ ] `worktrees/foundry-localization`.
  - [ ] `worktrees/foundry-logging`.
  - [ ] `worktrees/foundry-ci-release`.
- [ ] Do not work on the same files from multiple worktrees at the same time.
- [ ] Recommended worktree command pattern:

```powershell
git worktree add ..\foundry-winui-migration feat/winui-migration
git worktree add ..\foundry-core-extraction -b feat/foundry-core-extraction feat/winui-migration
git worktree add ..\foundry-velopack -b feat/foundry-velopack feat/winui-migration
```

## Agent Execution Protocol

- [ ] When a step requires maintainer-side action, guide the maintainer step by step:
  - [ ] Provide the exact command or UI action.
  - [ ] Explain the expected result.
  - [ ] Continue only after the action is confirmed or observable.
- [ ] For every implementation step, assess whether logging is needed:
  - [ ] Do not add logs automatically just because code changed.
  - [ ] Add logs only when they provide operational or diagnostic value.
  - [ ] Use the project logging levels consistently: `Debug`, `Information`, `Warning`, `Error`, and `Fatal`.
  - [ ] Use `Debug` only for developer diagnostics gated by developer mode.
  - [ ] Do not use `Verbose` unless a future approved subsystem proves it needs trace-level diagnostics.
  - [ ] Do not log noisy UI interactions, obvious control flow, secrets, tokens, passwords, encrypted secret payloads, media keys, or sensitive user data.
- [ ] When an implementation step is complete:
  - [ ] Run the relevant validation commands.
  - [ ] Review the changed files and exclude unrelated user changes.
  - [ ] Commit automatically using a focused Conventional Commit message.
  - [ ] Push the branch automatically.
  - [ ] Open the PR automatically against the target branch defined by this workflow.
- [ ] Prefer scoped Conventional Commit messages and PR titles when the scope is clear, for example:
  - [ ] `feat(winpe): add Intel wireless supplement catalog`.
  - [ ] `fix(packaging): include WinUI XAML assets`.
  - [ ] `refactor(logging): simplify startup log output`.
  - [ ] `docs(migration): clarify phase validation`.
- [ ] Do not ask for extra approval before commit, push, or PR creation once the user has requested execution of a migration step.
- [ ] Stop before commit, push, or PR creation only when validation fails, the branch contains unrelated changes, or the requested scope is ambiguous.

## Parallelization Rules

- [ ] Safe to run in parallel:
  - [ ] Core extraction after project skeleton exists.
  - [ ] WinUI shell import after release freeze is done.
  - [ ] CI workflow draft after target solution shape is defined.
  - [ ] Localization design after resource strategy is decided.
  - [ ] Logging design after startup model is decided.
  - [ ] Documentation updates after phase names stabilize.
- [ ] Do not run in parallel:
  - [ ] Solution/project file restructuring and release workflow restructuring.
  - [ ] Renaming/replacing `src\Foundry` and migrating XAML into the same folder.
  - [ ] Velopack update integration and old `ApplicationUpdateService` removal.
  - [ ] Localization resource conversion and view/page migration for the same screen.

## Recommended Logical Execution Order

Use the numeric phase IDs for stable prompts, but execute the work in dependency order when a later phase unblocks an earlier UI phase.

- [x] **Foundation lane**
  - [x] Phase 1: freeze scheduled releases.
  - [x] Phase 2: define repository and project boundaries.
  - [x] Phase 3: archive WPF Foundry and import WinUI shell.
  - [x] Phase 4: create `Foundry.Core` and clean tests.
  - [x] Phase 5: define extraction boundaries from the WPF reference.
- [x] **Application infrastructure lane**
  - [x] Phase 6: production startup, DI, app settings, navigation guard registration.
  - [x] Phase 10: production logging, because later ADK/media/update work must be diagnosable.
  - [x] Phase 7: Velopack distribution and update flow.
  - [x] Phase 8: CI and release workflow update.
- [x] **Shell and prerequisite lane**
  - [x] Phase 9: WinUI `.resw` localization foundation.
  - [x] Phase 11: DevWinUI shell navigation, pages, settings, and operation overlay.
  - [x] Phase 12: ADK, WinPE services, filesystem layout, and runtime layout normalization.
  - [x] Phase 13: Start page ISO/USB creation UI and media command wiring.
- [ ] **Business workflow lane**
  - [ ] Phase 14: Expert Deploy configuration and runtime compatibility.
  - [ ] Phase 15: network provisioning and `Foundry.Connect` handoff.
  - [ ] Phase 16: Autopilot and customization workflows.
- [ ] **Cutover lane**
  - [ ] Phase 17: `Foundry.Connect` and `Foundry.Deploy` compatibility pass.
  - [ ] Phase 18: cleanup and dependency review.
  - [ ] Phase 19: documentation update.
  - [ ] Phase 20: final cutover to `main`.

## Suggested Independent Workstreams

### Workstream A: Project And Build Infrastructure

- [x] Phase 1.
- [x] Phase 2.
- [x] Phase 3.
- [x] Phase 8.
- [ ] Phase 20.

### Workstream B: Core Extraction

- [x] Phase 4.
- [x] Phase 5.
- [x] Phase 12 pure services.
- [ ] Phase 14 Deploy configuration logic.
- [ ] Phase 15 provisioning logic.

### Workstream C: WinUI Shell

- [x] Phase 6.
- [x] Phase 11.
- [x] Phase 13 UI.
- [ ] Phase 14 UI.
- [ ] Phase 16 UI.

### Workstream D: Distribution

- [x] Phase 7.
- [x] Phase 8 release packaging.
- [ ] Phase 19 installation/update docs.

### Workstream E: Localization And Logging

- [x] Phase 9.
- [x] Phase 10.

## Suggested PR Sequence

- [x] PR 1: pause scheduled release.
- [x] PR 2: define layout and ignore prototype artifacts.
- [x] PR 3: archive WPF app and import WinUI shell.
- [x] PR 4: add `Foundry.Core` and move pure models/config logic.
- [x] PR 5: production WinUI startup, DI, app settings, and startup readiness baseline.
- [x] PR 6: Velopack packaging and manual update flow.
- [x] PR 7: CI/release workflow update.
- [x] PR 8: WinUI `.resw` localization foundation.
- [x] PR 9: production logging contract.
- [x] PR 10: DevWinUI shell navigation, settings, update banner, and blocking operation overlay.
- [x] PR 11: ADK status, ADK page, and navigation readiness integration.
- [x] PR 12: WinPE service foundations.
- [x] PR 13: Connect/Deploy runtime layout normalization.
- [x] PR 14: new `ProgramData`, ISO, USB, and WinPE media layout enforcement.
- [x] PR 15: Start page media creation UI and dry-run summary.
- [ ] PR 16: Expert Deploy configuration workflow.
- [ ] PR 17: network provisioning and `Foundry.Connect` handoff workflow.
- [ ] PR 18: final Start page ISO/USB command enablement after Phase 15.
- [ ] PR 19: Autopilot and customization workflows.
- [ ] PR 20: Connect/Deploy compatibility fixes.
- [ ] PR 21: cleanup and docs.
- [ ] PR 22: final cutover and scheduled release restoration.

# Migration Workflow

## Recommended Branch And Worktree Strategy

- [ ] Keep `main` as the stable reference branch.
- [ ] Create one long-lived integration branch for the migration:
  - [ ] `feat/winui-migration`.
- [ ] Use short-lived feature branches or worktrees for each independent migration phase.
- [ ] Merge short-lived branches into `feat/winui-migration` through PRs.
- [ ] Run the full CI matrix for PRs targeting `feat/winui-migration`.
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
- [ ] When an implementation step is complete:
  - [ ] Run the relevant validation commands.
  - [ ] Review the changed files and exclude unrelated user changes.
  - [ ] Commit automatically using a focused Conventional Commit message.
  - [ ] Push the branch automatically.
  - [ ] Open the PR automatically against the target branch defined by this workflow.
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

- [ ] **Foundation lane**
  - [ ] Phase 1: freeze scheduled releases.
  - [ ] Phase 2: define repository and project boundaries.
  - [ ] Phase 3: archive WPF Foundry and import WinUI shell.
  - [ ] Phase 4: create `Foundry.Core` and clean tests.
  - [ ] Phase 5: define extraction boundaries from the WPF reference.
- [ ] **Application infrastructure lane**
  - [ ] Phase 6: production startup, DI, app settings, navigation guard registration.
  - [ ] Phase 10: production logging, because later ADK/media/update work must be diagnosable.
  - [ ] Phase 7: Velopack distribution and update flow.
  - [ ] Phase 8: CI and release workflow update.
- [ ] **Shell and prerequisite lane**
  - [ ] Phase 9: WinUI `.resw` localization foundation.
  - [ ] Phase 11: DevWinUI shell navigation, pages, settings, and operation overlay.
  - [ ] Phase 13: ADK, WinPE services, filesystem layout, and runtime layout normalization.
  - [ ] Phase 12: Start page ISO/USB creation UI and media command wiring.
- [ ] **Business workflow lane**
  - [ ] Phase 14: configuration import/export and expert document compatibility.
  - [ ] Phase 15: network provisioning and `Foundry.Connect` handoff.
  - [ ] Phase 16: Autopilot and customization workflows.
- [ ] **Cutover lane**
  - [ ] Phase 17: `Foundry.Connect` and `Foundry.Deploy` compatibility pass.
  - [ ] Phase 18: cleanup and dependency review.
  - [ ] Phase 19: documentation update.
  - [ ] Phase 20: final cutover to `main`.

## Suggested Independent Workstreams

### Workstream A: Project And Build Infrastructure

- [ ] Phase 1.
- [ ] Phase 2.
- [ ] Phase 3.
- [ ] Phase 8.
- [ ] Phase 20.

### Workstream B: Core Extraction

- [ ] Phase 4.
- [ ] Phase 5.
- [ ] Phase 13 pure services.
- [ ] Phase 14 serialization logic.
- [ ] Phase 15 provisioning logic.

### Workstream C: WinUI Shell

- [ ] Phase 6.
- [ ] Phase 11.
- [ ] Phase 12 UI.
- [ ] Phase 14 UI.
- [ ] Phase 16 UI.

### Workstream D: Distribution

- [ ] Phase 7.
- [ ] Phase 8 release packaging.
- [ ] Phase 19 installation/update docs.

### Workstream E: Localization And Logging

- [ ] Phase 9.
- [ ] Phase 10.

## Suggested PR Sequence

- [ ] PR 1: pause scheduled release.
- [ ] PR 2: define layout and ignore prototype artifacts.
- [ ] PR 3: archive WPF app and import WinUI shell.
- [ ] PR 4: add `Foundry.Core` and move pure models/config logic.
- [ ] PR 5: production WinUI startup, DI, app settings, and logging baseline.
- [ ] PR 6: WinUI `.resw` localization foundation.
- [ ] PR 7: DevWinUI shell navigation, settings, and blocking operation overlay.
- [ ] PR 8: Velopack packaging and manual update flow.
- [ ] PR 9: CI/release workflow update.
- [ ] PR 10: WinPE orchestration, ADK page, and new filesystem layout.
- [ ] PR 11: Start page media creation workflow.
- [ ] PR 12: expert configuration import/export workflow.
- [ ] PR 13: network provisioning and `Foundry.Connect` handoff workflow.
- [ ] PR 14: Autopilot and customization workflows.
- [ ] PR 15: Connect/Deploy compatibility fixes.
- [ ] PR 16: cleanup and docs.
- [ ] PR 17: final cutover and scheduled release restoration.

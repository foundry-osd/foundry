# Task Plan: Foundry WinUI 3 Migration Study

## Goal
Prepare a code-informed migration study for moving only `src/Foundry` from WPF to WinUI 3 on .NET 10 while keeping `Foundry.Connect` and `Foundry.Deploy` as WPF projects.

## Current Phase
Phase 8: Final Plan Delivery

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

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Initial planning files were created in the primary checkout because `apply_patch` used the thread default directory | 1 | Removed only those plan artifacts from the primary checkout and recreated them in the dedicated worktree. |

## Notes
- Subagents must remain read-only.
- Any checkpoint commits in this phase must contain only planning artifacts.
- Implementation must wait for explicit user validation.

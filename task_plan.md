# Task Plan: Foundry WinUI 3 Migration Study

## Goal
Prepare a code-informed migration study for moving only `src/Foundry` from WPF to WinUI 3 on .NET 10 while keeping `Foundry.Connect` and `Foundry.Deploy` as WPF projects.

## Current Phase
Phase 1: Repository Topology and Build Discovery

## Constraints
- Plan phase only.
- No implementation.
- No code, project, solution, workflow, script, packaging, asset, or test changes.
- Planning artifacts may be written.
- Read-only subagents are allowed for inventory and impact analysis.
- Use current documentation for WinUI 3 and .NET guidance.

## Phases

### Phase 1: Repository Topology and Build Discovery
- [ ] Inspect solution structure.
- [ ] Inspect shared MSBuild configuration.
- [ ] Validate application and test project target frameworks and WPF usage.
- [ ] Record build topology findings.
- **Status:** in_progress

### Phase 2: Foundry Application Migration Surface
- [ ] Inspect startup and application lifetime.
- [ ] Inspect DI, host, services, shell, dialogs, theme handling, dispatching, resources, and XAML.
- [ ] Identify direct and hidden WPF dependencies.
- [ ] Record migration surface findings.
- **Status:** pending

### Phase 3: Architecture Quality Review
- [ ] Assess MVVM boundaries.
- [ ] Assess viewmodel purity and service boundaries.
- [ ] Identify areas to keep, adapt, redesign, replace, or remove.
- **Status:** pending

### Phase 4: Cross-Project Impact Review
- [ ] Inspect `Foundry.Connect`.
- [ ] Inspect `Foundry.Deploy`.
- [ ] Trace shared assets, build assumptions, publish assumptions, and runtime coupling.
- **Status:** pending

### Phase 5: Packaging, Publish, Release, and Workflow Review
- [ ] Inspect GitHub workflows.
- [ ] Inspect scripts and publish/release/archive logic.
- [ ] Identify WinUI 3 operational differences for this repository.
- **Status:** pending

### Phase 6: Current Documentation Check
- [ ] Use Context7 for current WinUI 3 and Windows App SDK guidance.
- [ ] Use authoritative sources if Context7 is insufficient for publish or packaging behavior.
- [ ] Record external documentation references.
- **Status:** pending

### Phase 7: Strategy, Risks, and Decision Matrix
- [ ] Build keep/adapt/redesign/replace/remove matrix.
- [ ] Build staged migration strategy.
- [ ] List risks, blockers, unknowns, and validation decisions.
- **Status:** pending

### Phase 8: Final Plan Delivery
- [ ] Produce final repository-specific analysis in the requested structure.
- [ ] Confirm no implementation was performed.
- [ ] Confirm only planning files were modified.
- **Status:** pending

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

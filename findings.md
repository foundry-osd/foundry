# Findings & Decisions

## Requirements
- Rework deployment progress page to match old operator-focused design.
- Keep all code and UI text in English.
- Add a centered global percentage with a custom ring.
- Keep a horizontal step progress bar for measurable sub-progress.
- Remove obsolete detailed progress artifacts where no longer needed.

## Research Findings
- Existing project uses native WPF with Fluent theme resources.
- No existing `ProgressRing` control dependency is present in the project.
- `DeploymentOrchestrator` already emits step and global progress events; download progress is measurable.
- DISM/apply steps do not expose reliable percent progress in current service contracts.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Added `CustomProgressRing` control | Provides ring + centered percentage without new package dependencies. |
| Extended `DeploymentStepProgress` with step sub-progress metadata | Enables a meaningful secondary bar for step-level progress. |
| Updated orchestrator download reporting to emit step sub-progress | Supports measured download progress while other steps stay indeterminate. |
| Removed old step list and deferred warning UI from progress page | Simplifies the UX and aligns with requested design direction. |
| Implemented cleanup via `IDisposable` in view model + window close hook | Prevents event/timer leaks and stale updates. |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Need a ring control but no library present | Implemented a custom WPF control with arc geometry and indeterminate animation. |

## Resources
- Context7: `/dotnet/wpf` guidance for dependency property patterns and custom control structure.

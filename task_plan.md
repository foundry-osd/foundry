# Task Plan: Refactor Foundry Working Directories (No ProgramData)

## Goal
Implement the approved path strategy across WinPE bootstrap and Foundry.Deploy (ISO + USB cache mode), removing all ProgramData usage and aligning cache/log behavior with the agreed workflow.

## Current Phase
Phase 5

## Phases

### Phase 1: Discovery & Baseline Lock
- [x] Confirm current path usage in Foundry, Foundry.Deploy, WinPE bootstrap, and 7zip provisioning
- [x] Confirm OS catalog hash fields availability
- [x] Record required change set and constraints
- **Status:** complete

### Phase 2: Bootstrap + WinPE Path Refactor
- [x] Remove ProgramData path constants and switch embedded/runtime roots
- [x] Update FoundryBootstrap.ps1 mode detection + runtime roots + log root
- [x] Update WinPE media provisioning paths for embedded deploy archive and 7zip tools
- **Status:** complete

### Phase 3: Foundry.Deploy Path + Workflow Refactor
- [x] Update cache roots and defaults (UI + runtime)
- [x] Reorder deployment workflow (partition before download)
- [x] Implement ISO/USB path resolution and log handoff flow
- [x] Update Autopilot deferred paths to Windows Temp Foundry (no ProgramData)
- **Status:** complete

### Phase 4: Hash + Cache Behavior Updates
- [x] Extend OS catalog model/parser with sha1/sha256
- [x] Update artifact hashing pipeline to support sha1 or sha256 validation
- [x] Keep strict cache validation behavior
- **Status:** complete

### Phase 5: Verification + Delivery
- [x] Build/validate impacted projects
- [x] Verify no remaining ProgramData references in runtime path logic
- [x] Summarize changes and residual risks
- **Status:** complete

## Key Questions
1. Any remaining runtime path references to ProgramData after refactor?
2. Is the reordered deployment flow still coherent with WindowsDeploymentService contract?
3. Are hashes correctly validated for both OS and DriverPack artifacts?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Remove ProgramData everywhere | User explicit requirement |
| WinPE root is X:\Foundry | Agreed baseline and temporary runtime area |
| USB mode detected by label Foundry Cache | Existing and agreed detection strategy |
| Autopilot deferred store is <SystemDrive>:\Windows\Temp\Foundry\Autopilot | Persistent after first boot without ProgramData |
| Keep cache-index.json on USB runtime | Persistent cache bookkeeping across sessions |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| None blocking | 1 | N/A |

## Notes
- Do not reintroduce legacy path compatibility.
- Keep 7zip provisioning and bootstrap extraction paths consistent.

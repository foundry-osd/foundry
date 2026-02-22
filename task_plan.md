# Task Plan: Foundry.Deploy WinPE Orchestrator

## Goal
Define and validate an implementation plan for a new `Foundry.Deploy` .NET 10 WPF project that orchestrates OS deployment in WinPE (wizard + progress/logging), inspired by OSDCloud, aligned with the current `Foundry` architecture and bootstrap flow.

## Current Phase
Phase 7

## Phases
### Phase 1: Requirements & Discovery
- [x] Understand user intent
- [x] Identify constraints and requirements
- [x] Document findings in findings.md
- **Status:** complete

### Phase 2: Existing Foundry Analysis
- [x] Inspect current solution structure and conventions
- [x] Inspect WinPE bootstrap script and packaging assumptions
- [x] Extract reusable UI/theme/infrastructure patterns
- **Status:** complete

### Phase 3: OSDCloud Deep Analysis
- [x] Analyze architecture and orchestration entry points
- [x] Analyze cache model and folder conventions
- [x] Analyze logging, error handling, and resiliency patterns
- **Status:** complete

### Phase 4: Foundry.Deploy Architecture Proposal
- [x] Propose project structure aligned with Foundry
- [x] Propose deployment workflow/state machine
- [x] Propose cache strategy for USB vs ISO modes
- [x] Propose bootstrap release/artifact contract
- **Status:** complete

### Phase 5: Delivery
- [x] Provide implementation roadmap with milestones
- [x] List open questions/decisions for user confirmation
- [x] Summarize risks and validation steps
- **Status:** complete

### Phase 6: Implementation Kickoff
- [x] Create `Foundry.Deploy` project skeleton aligned with Foundry architecture
- [x] Wire Fluent theme, DI, localization, and base wizard/progress UX
- [x] Implement WinPE bootstrap latest-release download via BITS
- [x] Validate solution/build integrity
- **Status:** complete

### Phase 7: Functional Deployment Implementation
- [x] Replace scaffold step bodies with real deployment actions (diskpart + DISM + BCDBoot + offline drivers)
- [x] Implement full Autopilot runtime workflow (hash export + online registration attempt + workflow transcript)
- [x] Implement persistent cache strategy with USB cache partition detection
- [x] Add structured logs and resume state files
- [ ] Add integration tests in WinPE-like environment
- **Status:** in_progress

## Key Questions
1. How closely should `Foundry.Deploy` replicate OSDCloud behavior vs keep only selected patterns?
2. How should cache ownership/persistence differ between USB and ISO mode in WinPE?
3. What release artifact contract is needed between `FoundryBootstrap.ps1` and GitHub Releases?
4. What UI/UX flow is mandatory in v1 vs optional for later milestones?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Use `planning-with-files` workflow | Task is multi-step research + architecture planning requiring persistent context |
| Require explicit target disk number in UI before deployment start | Prevents implicit destructive operations on an unintended disk |
| Execute destructive deployment actions in orchestrator (not placeholders) | Aligns with task-sequence V1 objective in WinPE |
| Fail deployment if full Autopilot is enabled and online registration fails | Enforces user requirement: "Autopilot complet" in V1 |
| Replace free-text disk number with assisted `Get-Disk` selection | Reduces operator error and blocks unsafe targets in UI |
| Require explicit runtime confirmation before disk erase | Adds final operator safety gate before `diskpart clean` |
| Enable automatic debug-safe dry-run when launched under Visual Studio debugger | Allows full UI/orchestrator navigation without destructive disk writes |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `rg` regex parse error (invalid escape sequence) | 1 | Re-ran searches with `rg -F` fixed-string patterns |
| PowerShell quoting conflict for literal `$($_.Name)` search | 2 | Used simpler `rg` patterns and direct file scans |
| `NETSDK1032` RID/platform mismatch on `Foundry.Deploy` | 1 | Moved single-file/self-contained settings to publish profiles |

## Notes
- Remaining gap is validation in a real WinPE lab run for disk/image/autopilot behavior.

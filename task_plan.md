# Task Plan: Refactor Foundry.Deploy DriverPack Pipeline

## Goal
Refactor the DriverPack flow in `Foundry.Deploy` into separate download, extract, and apply steps with progress reporting, safer DISM argument handling, and staged handling for deferred-install packages.

## Current Phase
Phase 5

## Phases

### Phase 1: Requirements & Discovery
- [x] Understand user intent
- [x] Identify constraints and requirements
- [x] Document findings in findings.md
- **Status:** complete

### Phase 2: Planning & Structure
- [x] Define technical approach
- [x] Create project structure if needed
- [x] Document decisions with rationale
- **Status:** complete

### Phase 3: Implementation
- [x] Add driver pack strategy and extraction infrastructure
- [x] Replace driver pack deployment steps
- [x] Add SetupComplete staging support
- [x] Fix DISM argument handling and progress reporting
- **Status:** complete

### Phase 4: Testing & Verification
- [x] Build the solution
- [x] Run focused verification
- [x] Fix any issues found
- **Status:** complete

### Phase 5: Delivery
- [x] Review changed files
- [x] Update planning artifacts
- [x] Deliver summary to user
- **Status:** complete

## Key Questions
1. How should package strategy be resolved across OEM and Microsoft Update Catalog payloads?
2. How should progress be surfaced consistently across download, extraction, and apply?
3. How should deferred-install packages coexist with Autopilot `SetupComplete.cmd` hooks?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Split DriverPack into 3 visible deployment steps | Matches requested UX and isolates responsibilities |
| Keep DISM for offline injection | Consistent with existing deployment code and avoids unnecessary PowerShell dependency |
| Add step-level progress for download, extract, and apply | Required by the user and already supported by current UI primitives |
| Use staged `SetupComplete.cmd` hooks for deferred packages | Matches OSDCloud behavior for Lenovo EXE and Surface MSI |
| Remove legacy `PreparedDriverPath` after the refactor stabilizes | Avoids keeping a transitional alias once all internal consumers have switched to `ExtractedDriverPackPath` |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
|       | 1       |            |

## Investigation: DISM Deployment Steps (2026-03-02)

### Goal
Identify which deployment steps invoke DISM directly or through `WindowsDeploymentService`, and determine which steps bundle multiple DISM phases that could benefit from resetting sub-progress between phases.

### Phases
- [x] Search deployment steps and DISM call sites
- [x] Trace step-to-service call chains
- [x] Summarize steps with single vs multi-phase DISM usage
- **Status:** complete

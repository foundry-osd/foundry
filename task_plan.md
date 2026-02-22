# Task Plan: Enforce 7-Zip-Only Extraction Across Foundry

## Goal
Use only 7-Zip for extraction paths across `Foundry` and `Foundry.Deploy`, remove extraction fallbacks, stop 7z runtime download in WinPE bootstrap, and provision embedded 7z assets from `src/Foundry/Assets/7z`.

## Current Phase
Phase 5

## Phases

### Phase 1: Discovery
- [x] Confirm user constraints (no download, no fallback)
- [x] Inventory all extraction actions in `src/Foundry` and `src/Foundry.Deploy`
- [x] Record findings in findings.md
- **Status:** complete

### Phase 2: Design
- [x] Define 7z provisioning model into WinPE image
- [x] Define shared extraction approach for host-side services
- [x] Decide failure behavior (hard-fail if 7z unavailable)
- **Status:** complete

### Phase 3: Implementation
- [x] Update WinPE bootstrap to use local bundled 7z only
- [x] Update WinPE image customization/provisioning to copy bundled 7z binaries
- [x] Replace non-7z extraction in `Foundry` and `Foundry.Deploy`
- **Status:** complete

### Phase 4: Verification
- [x] Build/compile impacted projects
- [x] Run targeted validation for extraction code paths
- [x] Capture remaining risks
- **Status:** complete

### Phase 5: Delivery
- [x] Summarize modified files and behavior changes
- [x] Provide verification results and any gaps
- **Status:** complete

## Key Questions
1. Where are extraction operations currently implemented?
2. Which runtime locations need 7z binaries (host + WinPE image)?
3. What is the minimal, deterministic no-fallback behavior?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Use `src/Foundry/Assets/7z` as single source for bundled 7z binaries | User-provided requirement; removes runtime download dependency |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| None yet |  |  |

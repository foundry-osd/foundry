# Task Plan: Native Microsoft Update Catalog in Foundry.Deploy

## Goal
Implement a native C# Microsoft Update Catalog workflow in Foundry.Deploy for driver and firmware updates, replace the current OSD PowerShell wrapper, and clean obsolete code created by that transition.

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
- [x] Identify impacted services, models, workflow steps, and UI
- [x] Document decisions with rationale
- **Status:** complete

### Phase 3: Implementation
- [x] Add native Microsoft Update Catalog client and models
- [x] Replace existing driver download wrapper with native implementation
- [x] Add firmware download/apply steps and user option
- [x] Update hardware profile collection for battery, firmware id, and PnP data
- [x] Remove obsolete OSD-wrapper assumptions and simplify code
- **Status:** complete

### Phase 4: Testing & Verification
- [x] Build Foundry.Deploy
- [x] Run targeted verification for parsing and workflow integration
- [x] Fix any issues found
- **Status:** complete

### Phase 5: Delivery
- [x] Review modified files
- [ ] Summarize implementation and verification results
- [ ] Deliver to user
- **Status:** in_progress

## Key Questions
1. How should firmware be discovered and applied in Foundry.Deploy?
2. Where should firmware steps sit relative to the driver pack workflow?
3. Which existing code paths become obsolete once the native Catalog client is added?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Implement Microsoft Update Catalog natively in C# | Removes dependency on the OSD PowerShell module and matches the requested design |
| Keep driver behavior as OEM first, Catalog fallback | Preserves current Foundry behavior while modernizing the Catalog path |
| Implement firmware as separate download/apply steps after driver pack apply | Matches the finalized plan and keeps firmware optional and isolated |
| Use OSDCloud's modern firmware identifier strategy | OSDCloud extracts a firmware GUID from the firmware PnP device and searches Catalog with it |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
|       | 1       |            |

## Notes
- Keep code in English.
- Simplify and remove obsolete OSD wrapper code while implementing the new native path.
- Use Context7-backed docs where they add value for implementation decisions.

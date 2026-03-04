# Task Plan: Implement GitHub Release Workflow and WinPE Cache Refresh

## Goal
Implement the planned GitHub release workflow, versioned Foundry release assets, and the WinPE bootstrap cache refresh behavior with a runtime-specific manifest-based cache.

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
- [x] Add GitHub release workflow
- [x] Align publish scripts and publish-related code
- [x] Refactor WinPE bootstrap cache logic
- [x] Remove obsolete code paths
- **Status:** complete

### Phase 4: Testing & Verification
- [x] Verify all requirements met
- [x] Document test results in progress.md
- [x] Fix any issues found
- **Status:** complete

### Phase 5: Delivery
- [ ] Review all output files
- [ ] Ensure deliverables are complete
- [ ] Deliver to user
- **Status:** in_progress

## Key Questions
1. How should release assets be named and packaged per runtime?
2. How should the bootstrap validate and refresh a runtime-specific cached deployment?
3. Which publish properties should be kept for consistent single-file output?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Use `release.published` as the release trigger | Matches the user's release flow and existing manual tagging process |
| Use the release tag as the only version source | Keeps versioning deterministic and aligned with published releases |
| Store cache metadata in a plain `manifest` file | PowerShell-friendly and sufficient for flat metadata |
| Only download the matching runtime deploy zip | Avoids unnecessary downloads and keeps WinPE architecture-specific |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `ConvertFrom-Yaml` unavailable locally | 1 | Switched to manual review and other verification steps |
| PyYAML not installed locally | 2 | Stopped YAML parser attempts and relied on manual inspection |

## Notes
- All new or updated code should use English identifiers, comments, and logs.
- Simplify logic where possible and remove dead code introduced by the refactor.

# Task Plan: WinPE Media Pipeline End-to-End

## Goal
Implement full WinPE media pipeline in Foundry: build/customize WinPE, optional driver catalog/injection, ISO output, safe USB output with removable targeting + double confirmation, PCA2011/PCA2023 handling, UI integration, and tests.

## Current Phase
Phase 8

## Phases

### Phase 1: Discovery & Architecture
- [x] Inspect repository integration points.
- [x] Validate operation/progress constraints.
- [x] Confirm service/UI/test integration paths.
- **Status:** complete

### Phase 2: Contracts & DTO Foundation
- [x] Establish WinPE contracts/options/results.
- [x] Register services in DI and initial ViewModel integration.
- **Status:** complete

### Phase 3: WinPE Build Execution
- [x] Implement ADK tool resolution and process execution helpers.
- [x] Implement `copype` workspace build and artifact model.
- [x] Implement safe mount/unmount flow for `boot.wim`.
- [x] Implement bootstrap script injection path.
- **Status:** complete

### Phase 4: Driver Catalog + Injection
- [x] Parse unified XML catalog from Foundry.Automation URI.
- [x] Filter by architecture/vendor.
- [x] Download package + SHA256 validation when available.
- [x] Extract CAB/EXE and inject via DISM.
- **Status:** complete

### Phase 5: ISO/USB Media Output
- [x] Implement ISO output via `MakeWinPEMedia`.
- [x] Implement USB safe provisioning + copy + verification.
- [x] Enforce disk identity + double confirmation rules.
- [x] Implement BOOT FAT32 + Foundry Cache NTFS layout.
- **Status:** complete

### Phase 6: PCA2023 Handling + Fallback
- [x] Detect `/bootex` capability.
- [x] Use `/bootex` for ISO when supported.
- [x] Implement remediation script fallback when required.
- [x] Persist output metadata.
- **Status:** complete

### Phase 7: UI/Localization Integration
- [x] Add UI options for staging/output/arch/signature/vendor.
- [x] Add USB disk selection and confirmation fields.
- [x] Add localization keys in EN/FR/base resources.
- **Status:** complete

### Phase 8: Tests + Verification
- [x] Add `Foundry.Tests` xUnit project.
- [x] Add validation/safety tests for WinPE services.
- [x] Run build and test suite successfully.
- **Status:** complete

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Partial backend checkpoint from worker failed build (missing imports + stale stubs) | 1 | Completed backend manually and restored green build/tests |
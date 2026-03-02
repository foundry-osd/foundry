# Findings & Decisions

## Requirements
- Split the DriverPack flow into download, extract, and apply stages.
- Show progress in the UI for all three stages when possible.
- Use the OSDCloud handling model as reference.
- Exploit 7-Zip availability in the boot image for extraction scenarios.
- Fix the current offline driver apply reliability issue.

## Research Findings
- The current code combines download and extraction in a single step.
- `ApplyOfflineDriversAsync` currently builds a raw DISM argument string, which is fragile for roots such as `W:\`.
- The driver catalog is dominated by `.exe` payloads, so treating only `.cab` and `.zip` as extractable is insufficient.
- OSDCloud handles CAB/ZIP as offline-injectable, HP EXE with 7-Zip extraction, Lenovo EXE as deferred, and Surface MSI as deferred.
- `Foundry.Deploy` builds successfully after the refactor.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Introduce explicit strategy and extraction services | Keeps step logic thin and testable |
| Preserve `PreparedDriverPath` temporarily as a compatibility alias | Reduces regression risk while new state fields are introduced |
| Add a reusable `SetupComplete` service | Avoids duplicating marker/idempotence logic with Autopilot |
| Use 7-Zip progress parsing when possible and weighted/jump progress otherwise | Keeps extraction progress visible even when native progress is unavailable |
| Split Microsoft Update Catalog into download and expand methods | Enables distinct deployment steps and separate progress states |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Context7 did not return the specific `ProcessStartInfo.ArgumentList` page directly | Relied on local implementation in `ProcessRunner` as the source of truth for safe argument separation |

## Resources
- `src/Foundry.Deploy/Services/Deployment/*`
- `src/Foundry.Deploy/Services/DriverPacks/*`
- `https://github.com/OSDeploy/OSDCloud`
- Context7 `.NET` and PowerShell documentation queries already used during planning

## Visual/Browser Findings
- OSDCloud’s `step-drivers-driverpack.ps1` separates download, expansion, and apply logic implicitly by package type and stages deferred packages via `SetupComplete.cmd`.

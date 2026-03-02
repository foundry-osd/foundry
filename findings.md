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
- A second pass found the old preparation layer (`IDriverPackPreparationService`, `DriverPackPreparationService`, `DriverPackPreparationResult`) fully orphaned after the refactor and safe to delete.
- A third cleanup pass confirmed the remaining `PreparedDriverPath` compatibility alias was fully internal and safe to remove.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Introduce explicit strategy and extraction services | Keeps step logic thin and testable |
| Remove `PreparedDriverPath` once the refactor is internally consistent | Avoids carrying a redundant state alias and duplicate summary data |
| Add a reusable `SetupComplete` service | Avoids duplicating marker/idempotence logic with Autopilot |
| Use 7-Zip progress parsing when possible and weighted/jump progress otherwise | Keeps extraction progress visible even when native progress is unavailable |
| Split Microsoft Update Catalog into download and expand methods | Enables distinct deployment steps and separate progress states |
| Reset step sub-progress by phase for multi-operation servicing steps | Makes repeated DISM operations visible (`mount`, `apply`, `unmount`) instead of forcing one monotonically increasing sub-bar |

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

## Investigation: DISM Deployment Steps (2026-03-02)

### Requirements
- Map deployment steps to DISM operations, including indirect calls through `WindowsDeploymentService`.
- Flag steps that run more than one DISM sub-operation in a single step and could use sub-progress resets between phases.

### Research Findings
- `Foundry.Deploy` registers the deployment pipeline steps in `Program.cs`; the relevant DISM-capable steps are `ApplyOperatingSystemImageStep` and `ApplyDriverPackStep`.
- `ApplyOperatingSystemImageStep` chains three `IWindowsDeploymentService` calls that reach `dism.exe`: `ResolveImageIndexAsync` (`/Get-ImageInfo`), `ApplyImageAsync` (`/Apply-Image`), and `GetAppliedWindowsEditionAsync` (`/Get-CurrentEdition`).
- `ApplyDriverPackStep` in `OfflineInf` mode always calls `ApplyOfflineDriversAsync` (`/Add-Driver` for the offline Windows image), and when WinRE is configured it also calls `ApplyRecoveryDriversAsync`, which performs `dism.exe /Mount-Image`, `dism.exe /Add-Driver`, and `dism.exe /Unmount-Image`.
- No deployment step class launches `dism.exe` directly; the step classes route through `IWindowsDeploymentService`, and the actual DISM process launches are concentrated in `WindowsDeploymentService`.

## Investigation: Deployment Progress Dead-Code Review (2026-03-02)

### Requirements
- Check the recent deployment-progress commits for the named deployment service/step types.
- Distinguish definitely dead or unused members from benign redundancy.

### Research Findings
- No definitely dead or unreachable members were found in `IWindowsDeploymentService`, `WindowsDeploymentService`, `ApplyDriverPackStep`, `ApplyOperatingSystemImageStep`, or `DismProgressReporter` after tracing the current call sites.
- The newly split `ApplyRecoveryDriversAsync` progress parameters (`mountProgress`, `applyProgress`, `unmountProgress`) are live end-to-end: declared in the interface, passed by `ApplyDriverPackStep`, and consumed in `WindowsDeploymentService`.
- The likely cleanup candidate `MapProgress` in `ApplyDriverPackStep` is still used by deferred driver-pack staging, so it is not orphaned.
- The only noticeable leftovers are low-risk redundancies: several explicit terminal `Report(100d)` calls can become no-ops when DISM already reported 100%, and the same `DismProgressReporter.HandleOutput` callback is intentionally attached to both stdout and stderr.

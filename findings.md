# Findings & Decisions

## Requirements
- Implement approved working-directory plan end-to-end.
- Remove ProgramData usage from runtime/deploy/bootstrap paths.
- Keep ISO and USB cache modes.
- USB detection by disk label `Foundry Cache`.
- Keep 7zip extraction/provisioning functional for x64/arm64.
- Reorder deployment flow so partition/init happens before downloads.
- Logs flow: X:\Foundry\Logs -> <SystemDrive>:\Foundry\Logs -> <SystemDrive>:\Windows\Temp\Foundry.

## Research Findings
- FoundryBootstrap currently uses `X:\ProgramData\Foundry\Deploy` roots.
- WinPE defaults currently embed deploy archive + 7zip in `ProgramData\Foundry\Deploy\...` paths.
- Foundry.Deploy cache defaults still use legacy `C:\Foundry\Deploy` and `X:\Windows\Temp\Foundry\Deploy`.
- OSDCloud workflow order confirms partition before install/download is a valid model.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| No ProgramData paths at all | Explicit user requirement |
| Transient WinPE root `X:\Foundry` | Central pre-partition workspace |
| USB runtime `<CacheDrive>:\Runtime` | Persistent media-local runtime |
| ISO runtime `<SystemDrive>:\Foundry` after partition | Matches user target and avoids pre-partition ambiguity |
| Autopilot deferred path under Windows Temp Foundry | Persisted path with no ProgramData usage |

## Implemented Changes
- WinPE defaults now embed deploy seed and 7zip under `X:\Foundry\...` image paths (no ProgramData).
- Bootstrap script now resolves runtime dynamically:
  - USB cache mode: `<CacheDrive>:\Runtime` (label or marker `Foundry Cache`)
  - fallback: `X:\Foundry\Runtime`
  - log path: `X:\Foundry\Logs\FoundryBootstrap.log`
- USB provisioning now creates:
  - `<CacheDrive>:\Foundry Cache` (marker)
  - `<CacheDrive>:\Runtime`
  - `<CacheDrive>:\OperatingSystem`
  - `<CacheDrive>:\DriverPack`
- Foundry.Deploy cache defaults and startup logging moved to `X:\Foundry\...`.
- Deployment orchestrator reordered to prepare target disk before downloads.
- OS downloads now validate hash from catalog (`sha256` preferred, fallback `sha1`).
- Artifact downloader supports SHA1 and SHA256 based on expected hash length.
- Autopilot deferred assets moved to `<Windows>\Temp\Foundry\Autopilot`.
- Finalization now writes `deployment-summary.json` into `<SystemDrive>:\Windows\Temp\Foundry` and copies logs/state there.

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| OS catalog hash ambiguity | Re-verified XML: items contain `sha1` or `sha256`; update model/parser required |

## Resources
- src/Foundry/Assets/WinPe/FoundryBootstrap.ps1
- src/Foundry/Services/WinPe/WinPeDefaults.cs
- src/Foundry/Services/WinPe/MediaOutputService.cs
- src/Foundry.Deploy/Services/Cache/CacheLocatorService.cs
- src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs
- src/Foundry.Deploy/Services/Autopilot/AutopilotService.cs
- src/Foundry.Deploy/Services/Catalog/OperatingSystemCatalogService.cs
- src/Foundry.Deploy/Models/OperatingSystemCatalogItem.cs

## Visual/Browser Findings
- OSDCloud default task order: preinstall partition happens before install/apply sequence.
- OSDCloud supports OS sha1/sha256 in its operating system object model.

---
*Update this file after every 2 view/browser/search operations*

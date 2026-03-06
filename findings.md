# Findings & Decisions

## Requirements
- Implement the previously agreed Microsoft Update Catalog plan in Foundry.Deploy.
- Use the `planning-with-files` skill workflow.
- Use Context7 during the task.
- Write code in English.
- Simplify or remove obsolete code that becomes unnecessary after the native implementation.
- Keep OEM driver packs as the preferred source and use Microsoft Update Catalog as fallback.
- Implement firmware as a separate optional workflow controlled by the user.
- Firmware UI option is checked by default, but unchecked and disabled on VMs.
- Firmware should use two separate steps: download, then apply.
- Firmware should run after the driver pack workflow.
- Skip firmware on battery.

## Research Findings
- Foundry currently implements Microsoft Update Catalog by invoking PowerShell and calling `Save-MsUpCatDriver` from the OSD module in `src/Foundry.Deploy/Services/DriverPacks/MicrosoftUpdateCatalogDriverService.cs`.
- Foundry currently treats the Microsoft Update Catalog path as a generic offline INF payload and has no native search, parsing, or hardware-targeted Catalog logic.
- OSDCloud modern driver logic lives in `Save-MicrosoftUpdateCatalogDriver.ps1` and uses `Win32_PnpEntity`, de-duplicates Hardware IDs, searches Catalog by Windows release tokens, downloads CAB payloads, and expands them into GUID-named folders.
- OSDCloud firmware logic is separate from drivers. It discovers firmware by finding the PnP firmware device with `ClassGuid {f2e7dd72-6468-4e36-b6f1-6488f42c1b52}` and extracting a GUID from `PNPDeviceID`.
- OSD/OSDCloud search Microsoft Update Catalog by scraping HTML from `Search.aspx` and retrieving actual file links from `DownloadDialog.aspx`; there is no public API.
- OSDCloud workflow order is `firmware download -> firmware apply -> OEM driver pack -> OEM apply -> Microsoft Update Catalog drivers -> Microsoft Update Catalog apply`, but the finalized Foundry plan places firmware after driver pack apply.
- OSDCloud blocks firmware on VMs and on battery.
- Foundry already has reusable HTTP retry and download infrastructure in `Services/Http` and `Services/Download`.
- Foundry's `DeploymentOrchestrator` validates both deployment step count and exact order against `DeploymentStepNames.All`, so adding firmware steps requires synchronized changes to step names, DI registration, and `Order` values.
- `MainWindowViewModel` already has a proven pattern for preserving user-driven selection state across refreshes via `_isUpdating...` and `_hasUserSelected...` flags; the firmware option should reuse that pattern.
- `IWindowsDeploymentService.ApplyOfflineDriversAsync` is sufficient for firmware apply because the agreed behavior is offline Windows-only injection, not WinRE.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Add a shared native Catalog client with HTML parsing | Both driver and firmware workflows need the same Search.aspx and DownloadDialog.aspx logic |
| Add explicit firmware workflow state to deployment context/runtime | The firmware steps need clear control and logging separate from drivers |
| Extend `HardwareProfile` with PnP and firmware metadata | Native discovery requires more than manufacturer/model/architecture |
| Keep firmware application limited to offline Windows, not WinRE | This matches the agreed plan and the OSDCloud firmware behavior |
| Use Html Agility Pack for Catalog parsing | Context7 resolved `/zzzprojects/html-agility-pack` as a suitable .NET HTML parser with tolerant DOM support |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| None yet | N/A |

## Resources
- OSD PowerShell module docs via Context7: `/osdeploy/osd`
- OSD repo: `C:\Users\mchav\AppData\Local\Temp\OSDRepo`
- OSDCloud repo: `C:\Users\mchav\AppData\Local\Temp\OSDCloudRepo`
- Foundry driver wrapper: `src/Foundry.Deploy/Services/DriverPacks/MicrosoftUpdateCatalogDriverService.cs`
- OSDCloud firmware workflow: `C:\Users\mchav\AppData\Local\Temp\OSDCloudRepo\private\steps\5-drivers\step-Save-WindowsDriver-Firmware.ps1`
- OSD firmware logic: `C:\Users\mchav\AppData\Local\Temp\OSDRepo\Public\Functions\SystemFirmware.ps1`

## Visual/Browser Findings
- Context7 confirms OSD exposes separate `Save-SystemFirmwareUpdate` and `Install-SystemFirmwareUpdate` operations.
- Context7 confirms `Get-MsUpCat` / OSD Catalog queries are driven by HTML parsing of the Microsoft Update Catalog site.

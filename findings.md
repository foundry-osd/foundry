# Findings & Decisions

## Requirements
- Document files defining Driver tab UI (XAML/views)
- Identify viewmodels/controllers powering Driver tab comboboxes
- Locate catalog logic building source/OEM options, handling model/version selection, and matching hardware models

## Research Findings
- Repository contains `scripts/` and `src/` directories; business logic likely under `src/Foundry`
- `src/Foundry` exposes WPF assets (App.xaml, MainWindow.xaml), services, converters, and a `ViewModels/` directory—Driver tab artifacts should live there
- `MainWindow` view binds driver pack checkboxes, custom driver directory path, and exposes comboboxes for languages, architectures, partition styles, and USB format modes via `MainWindowViewModel`
- `MainWindowViewModel` exposes selection-backed collections (e.g., `AvailableWinPeLanguages`, `AvailableUsbFormatModes`) plus helper `GetSelectedDriverVendors()` to derive catalog vendor selections from checkboxes
- `WinPeDriverCatalogService` fetches the catalog URI, validates options, and builds `WinPeDriverCatalogEntry` list filtered by requested vendors via `ParseDriverPacks`
- Driver-related UI sits within the standard configuration and advanced-expander sections in `MainWindow.xaml`, covering ISO path, USB candidate picker, architecture/language combo boxes, driver vendor checkboxes, partition style/format combo boxes, custom driver directory, and creation buttons
- Detailed line references were gathered for the combo bindings plus driver vendor checkboxes/custom path and for the viewmodel properties/methods so the final mapping can cite precise locations
- `MediaOutputService.ResolveDriversAsync` normalizes vendor selections, optionally fetches the catalog via `WinPeDriverCatalogService`, picks the latest package per vendor, hands them off to `WinPeDriverPackageService`, and merges custom driver directories to produce the final list passed to image customization
- `WinPeDriverCatalogService.ParseDriverPacks` filters `DriverPack` elements by manufacturer, release ID, and architecture, builds `WinPeDriverCatalogEntry` records with version/release metadata, and orders them by release date before returning the list (`ParseVendor`/`ParseArchitecture` helpers normalize OEM and architecture text)
- `WinPeVendorSelection` exposes the OEM enum (Any, Dell, Hp, Lenovo, Microsoft) while `IsoOutputOptions`/`UsbOutputOptions` carry the `DriverCatalogUri` (default `WinPeDefaults.DefaultUnifiedCatalogUri`) plus the `DriverVendors` list that `MainWindowViewModel` fills via `GetSelectedDriverVendors`
- No dedicated `DriverTab` file was found; `MainWindow.xaml` centralizes driver-specific interfaces

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Prioritize XAML and ViewModel pairs near Driver tab namespace | These will most directly correlate to requested UI and logic |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Session-catchup script unavailable because `python` executable missing | Logged in task_plan.md and proceeding without script |

## Resources
- Repository root at c:\DEV\Github\Foundry

## Visual/Browser Findings
- None yet

---
*Update this file after every 2 view/browser/search operations*
- *This prevents visual information from being lost*

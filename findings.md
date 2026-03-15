# Findings & Decisions

## Requirements
- The user wants to brainstorm the Foundry project window architecture before implementation.
- The window should support a `Simple/Standard` mode and an `Expert` mode.
- The user wants a switch between modes.
- The top menu should remain present.
- In Expert mode, the user wants left-side navigation grouped by configuration category such as network and personalization.
- The current content under `standard configuration` and `advanced options` should effectively define the current Standard mode baseline.
- The architecture should stay modular so new advanced options can be added later.
- The user wants import/export of configuration.
- Advanced/exported configuration may need to be written to a separate file that `foundry deploy` can consume.
- For advanced options with sub-options, the parent checkbox should enable or disable the whole option group.
- When the parent option is disabled, child controls should be disabled.
- The user does not want a subproject or a separate test project.
- The user explicitly asked to use Context7.
- `Simple` and `Standard` are the same mode.
- In Expert mode, the user wants to see the same values as Standard plus more sections.
- Expert mode left navigation should contain categories only, not subpages.
- Initial expert categories should start with `Network`, `Localization`, and `Customization`.
- The mode switch should likely live in the top menu under a dedicated `Mode` entry.
- Import/export should be exposed as `File` menu actions.
- Expert mode should have two explicit exports:
  - a full Foundry expert config
  - a deploy-consumable JSON file
- The deploy-consumable file is not user-editable and should be generated automatically from Expert selections.
- The deploy-consumable file should likely live under `X:\Foundry\Config`.
- Standard mode should not inject any deploy-consumable file when building new media.
- When switching back to Standard mode, expert-only settings should not be applied and no warning/banner should be shown.
- For export, disabled child settings should be stripped from JSON.
- For `Network`, the first controls should cover 802.1X enablement, certificate import, and Wi-Fi in WinPE. Logic is out of scope for now; controls only.
- For `Localization`, the expert config should constrain which Windows language choices are exposed in `Foundry.Deploy`, support a default override, and optionally force the only available choice.
- The generated deploy file name should be explicit: `foundry.deploy.config.json`.
- Expert values should stay in memory when switching back to Standard mode.
- `Foundry.Deploy` should auto-load the generated expert config silently, with no banner.
- For `Customization`, machine naming should be the first implemented group; Autopilot, APPX, and custom-config areas can start as placeholders.
- `Foundry.Deploy` should consume the generated expert config through a dedicated optional loader service, not directly in `Program.cs`.
- The Expert localization registry should be stored as an embedded JSON resource in `Foundry`.
- In `Customization` v1, only `MachineNaming` should produce persisted/exported config; `Autopilot`, `APPX removal`, and `Custom deploy config` should remain UI-only placeholders.
- For `Customization`, the expert config should cover machine naming behavior and may later include Autopilot, config injection, and APPX removal.

## Research Findings
- The `planning-with-files` skill requires persistent project files: `task_plan.md`, `findings.md`, and `progress.md`.
- Initial repository inspection shows root folders: `.github`, `Assets`, `scripts`, and `src`.
- The environment cannot execute `rg.exe`; PowerShell search must be used instead.
- `Foundry` is a WPF desktop app targeting `net10.0-windows` and using `CommunityToolkit.Mvvm`.
- The current main window keeps a top `Menu` and uses a single flat page layout in `src/Foundry/MainWindow.xaml`.
- The current window already has two major sections that match the user's framing:
  - `Section: standard configuration`
  - `Section: advanced options`
- The existing advanced UI is currently a single `Expander`, not a category-based expert workspace.
- The current `MainWindowViewModel` directly exposes window state as observable properties rather than through grouped configuration objects.
- Current media creation already passes structured records to services:
  - `IsoOutputOptions`
  - `UsbOutputOptions`
- Those option records already mix booleans, enums, strings, and nullable string fields, which is a good base for a future export model.
- `Foundry.Deploy` already has a richer configuration surface, and its `DeploymentContext` uses concrete booleans such as `ApplyFirmwareUpdates`, `UseFullAutopilot`, and `AllowAutopilotDeferredCompletion`.
- The current `Foundry` window contains properties like `EnablePcaRemediation` and `PcaRemediationScriptPath` in the view model, which suggests some advanced settings are already anticipated even if the XAML does not surface them yet.
- Context7 confirmed the relevant library is official WPF (`/dotnet/wpf`). The returned guidance is generic, but it aligns with a container-based MVVM layout where grouped controls bind against view-model state.
- The WinPE/bootstrap side consistently treats `X:\Foundry` as the runtime root and already uses subfolders such as `Logs`, `Seed`, `Tools`, and `Runtime`.
- Based on the existing root layout, `X:\Foundry\Config` is a coherent location for injected expert-mode configuration files.
- `Foundry.Deploy` currently uses `X:\Foundry\Runtime` for transient runtime/cache behavior, so `Config` should be treated as input/configuration and `Runtime` as operational state.
- `Foundry.Deploy` currently sources operating system and language options from the operating system catalog service rather than a static built-in language registry.
- The deploy view model normalizes and selects language codes from the current catalog-backed filter list.
- Official .NET documentation via Context7 (`/dotnet/docs`) confirms `System.Text.Json` supports omitting null properties during serialization with `JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`.
- That JSON behavior fits the desired export rule: parent feature flag remains explicit, while disabled child values can be stripped from exported files by serializing them as `null` and omitting nulls on write.
- Official .NET documentation via Context7 also confirms that `System.Text.Json` ignores unknown JSON properties by default during deserialization, while .NET 8+ can be configured to disallow them.
- This makes forward-compatible import practical: ignore unknown fields by default, but validate required fields and consider surfacing a non-blocking notice.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Delay concrete UI recommendations until the current window implementation is inspected | Recommendations should fit the existing codebase rather than assume a stack or layout |
| Treat parent expert toggles as a first-class model concern, not only a UI concern | Enablement, serialization, import/export, and deploy generation all depend on the same state model |
| Favor introducing an explicit shared configuration model over exporting raw window state | The current view model is UI-oriented, while import/export and deploy integration need stable contracts |
| Preserve the top menu and treat Expert mode as a shell/layout change inside the main content area | This matches the user's preferred UX direction and the current XAML structure |
| Recommend preserving disabled child values in memory, but stripping them from exported JSON | Better UX for toggling settings on/off without losing work, while keeping export clean and semantically accurate |
| Recommend a top-menu `Mode` menu with mutually exclusive mode choices | The app already uses a top menu for app-wide concerns, and mode changes the whole shell rather than a local panel |
| Recommend ignoring unknown fields on expert-config import, with validation and a non-blocking notice | This is the best forward-compatibility tradeoff for user-owned config files |
| Recommend a hybrid localization source: built-in curated language registry for Expert config UI, validated/intersected with catalog-backed deploy choices when available | Keeps Expert configuration stable and offline-friendly without drifting completely away from deploy reality |
| Recommend a dedicated optional config loader service inside `Foundry.Deploy` | Startup config application is view-model/state logic, not bootstrap logic |
| Recommend embedded JSON for the built-in localization registry | Easier maintenance than hardcoded lists and more appropriate than environment config |
| Recommend keeping future customization groups UI-only until their contracts are real | Prevents speculative export schema growth |

## Implementation-Ready Architecture Draft

### 1. Window Shell
- Keep the existing top menu in `Foundry`.
- Add a new top-level `Mode` menu with two mutually exclusive items:
  - `Standard`
  - `Expert`
- Extend the existing `File` menu with:
  - `Import Expert Configuration`
  - `Export Expert Configuration`
  - `Export Deploy Configuration`
- Keep Standard mode visually close to the current single-page layout.
- In Expert mode, replace the current single-page content area with a two-column settings shell:
  - left column: category navigation
  - right column: category content host
- Implement left navigation with a simple MVVM-friendly item source such as a `ListBox`, and host the selected category content through a `ContentControl` with DataTemplates.
- Preserve the current status/progress/footer regions below the main configuration area.

### 2. Expert Categories
- `General`
  - Contains everything currently shown in Standard mode:
    - current `standard configuration`
    - current `advanced options`
- `Network`
  - 802.1X enablement
  - certificate import controls
  - Wi-Fi in WinPE controls
  - controls only in v1; no functional implementation yet
- `Localization`
  - visible language list for `Foundry.Deploy`
  - default language override
  - force single visible language option
- `Customization`
  - `MachineNaming` as the only persisted/exported v1 group
  - `Autopilot`, `APPX removal`, and `Custom deploy config` as UI-only placeholders

### 3. ViewModel Split
- Keep `MainWindowViewModel` as the shell-level coordinator.
- Move long-term settings state into a canonical configuration object rather than keeping everything as flat UI properties.
- Recommended shell responsibilities for `MainWindowViewModel`:
  - current mode
  - current expert category
  - top-menu commands
  - import/export commands
  - operation/progress state
  - adaptation between current window commands and configuration-backed values
- Recommended child view models or section models:
  - `GeneralSettingsViewModel`
  - `NetworkSettingsViewModel`
  - `LocalizationSettingsViewModel`
  - `CustomizationSettingsViewModel`
- Keep the Standard view bound to the same underlying config-backed state as Expert.
- Do not create a separate persisted Standard model.

### 4. Canonical Config Model
- Introduce one canonical persisted model inside `Foundry`, for example:
  - `FoundryExpertConfigurationDocument`
- Recommended top-level fields:
  - `schemaVersion`
  - `general`
  - `network`
  - `localization`
  - `customization`
- `schemaVersion` should be explicit from v1.
- The model should contain only persisted settings, not transient UI state such as:
  - current mode
  - selected navigation category
  - expander open/closed state
  - progress text
- Standard mode edits should write into the same underlying settings represented by `general`.

### 5. Parent/Child Toggle Pattern
- For any Expert feature with sub-options, use a consistent persisted shape:
  - `isEnabled`
  - child properties
- UI behavior:
  - parent checkbox controls `isEnabled`
  - child container binds `IsEnabled` to the parent toggle
  - when disabled, child values stay in memory
- Export behavior:
  - parent `isEnabled` stays explicit
  - child values are omitted when disabled by serializing them as `null` and using null-ignore serialization

### 6. Full Expert Config Export
- `Foundry` full expert config is user-owned and importable.
- Recommended file purpose:
  - long-term editable/exportable expert settings snapshot
- Recommended characteristics:
  - JSON
  - includes `schemaVersion`
  - ignores unknown fields on import
  - validates known required fields
  - omits disabled child values

### 7. Deploy Config Export
- Generate a second, narrower JSON contract specifically for `Foundry.Deploy`.
- Recommended file path when injected into media:
  - `X:\Foundry\Config\foundry.deploy.config.json`
- Standard mode:
  - do not generate or inject this file
- Expert mode:
  - generate and inject this file
- This deploy contract should contain only values that `Foundry.Deploy` actually consumes.

### 8. `Foundry.Deploy` Consumption Contract
- Add a dedicated optional loader service in `Foundry.Deploy`, for example:
  - `IExpertDeployConfigLoader`
- Responsibilities:
  - read `X:\Foundry\Config\foundry.deploy.config.json` if present
  - deserialize it
  - validate it
  - expose a typed result to `MainWindowViewModel`
- Apply config in two stages:
  - stage 1: immediate scalar values
  - stage 2: catalog-dependent values after catalog data is loaded
- Startup behavior:
  - silent
  - no banner
  - logging only
- Invalid or unknown fields:
  - unknown fields ignored
  - known invalid values logged and skipped
  - app continues with safe defaults

### 9. Embedded Localization Registry
- Keep the Expert localization registry inside `Foundry` as an embedded JSON resource.
- Recommended data shape per language entry:
  - `code`
  - `displayName`
  - `englishName`
  - optional `sortOrder`
- Use this registry for the Expert editor UI only.
- During deploy export or deploy load, validate/intersect language codes against real deploy-supported options.

### 10. Suggested File/Folder Additions
- `src/Foundry/Models/Configuration/FoundryExpertConfigurationDocument.cs`
- `src/Foundry/Models/Configuration/GeneralSettings.cs`
- `src/Foundry/Models/Configuration/NetworkSettings.cs`
- `src/Foundry/Models/Configuration/LocalizationSettings.cs`
- `src/Foundry/Models/Configuration/CustomizationSettings.cs`
- `src/Foundry/Models/Configuration/MachineNamingSettings.cs`
- `src/Foundry/Models/Configuration/FeatureToggle*.cs` or equivalent grouped option models
- `src/Foundry/Services/Configuration/IExpertConfigurationService.cs`
- `src/Foundry/Services/Configuration/ExpertConfigurationService.cs`
- `src/Foundry/Services/Configuration/IDeployConfigurationGenerator.cs`
- `src/Foundry/Services/Configuration/DeployConfigurationGenerator.cs`
- `src/Foundry/Services/Configuration/ILanguageRegistryService.cs`
- `src/Foundry/Services/Configuration/EmbeddedLanguageRegistryService.cs`
- `src/Foundry/ViewModels/Expert/ExpertSectionItem.cs`
- `src/Foundry/ViewModels/Expert/GeneralSettingsViewModel.cs`
- `src/Foundry/ViewModels/Expert/NetworkSettingsViewModel.cs`
- `src/Foundry/ViewModels/Expert/LocalizationSettingsViewModel.cs`
- `src/Foundry/ViewModels/Expert/CustomizationSettingsViewModel.cs`
- `src/Foundry/Assets/Configuration/languages.json`
- `src/Foundry.Deploy/Services/Configuration/IExpertDeployConfigLoader.cs`
- `src/Foundry.Deploy/Services/Configuration/ExpertDeployConfigLoader.cs`
- `src/Foundry.Deploy/Models/Configuration/DeployExpertConfiguration.cs`

### 11. Suggested Delivery Order
- Step 1: introduce canonical config models and serialization services
- Step 2: add top-menu `Mode` and File-menu import/export commands
- Step 3: refactor `MainWindowViewModel` to bind through canonical config-backed state
- Step 4: implement Expert shell with left navigation and `General` category
- Step 5: add `Network`, `Localization`, and `Customization` categories
- Step 6: generate deploy-consumable JSON in Expert mode only
- Step 7: add optional silent loader service in `Foundry.Deploy`

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| `rg` search is unavailable due to an execution/access error | Use `Get-ChildItem` and `Select-String` |

## Open Questions / Potential Inconsistencies
- The exact sequencing for applying expert config versus catalog-loaded data in `Foundry.Deploy` still needs to be specified during technical design.
- The first embedded localization registry schema still needs to be defined precisely.
- The post-v1 priority order for future Expert groups still needs to be chosen.

## Resources
- Planning skill: `C:\Users\mchav\.codex\skills\planning-with-files\SKILL.md`
- Repo root: `E:\Github\Foundry`
- `E:\Github\Foundry\src\Foundry\MainWindow.xaml`
- `E:\Github\Foundry\src\Foundry\ViewModels\MainWindowViewModel.cs`
- `E:\Github\Foundry\src\Foundry\Services\WinPe\IsoOutputOptions.cs`
- `E:\Github\Foundry\src\Foundry\Services\WinPe\UsbOutputOptions.cs`
- `E:\Github\Foundry\src\Foundry.Deploy\Services\Deployment\DeploymentContext.cs`
- Context7 WPF docs: `/dotnet/wpf`

## Visual/Browser Findings
- None yet.

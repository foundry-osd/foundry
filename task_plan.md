# Task Plan: Foundry UI Restructure + Business Logic Enhancements

## Goal
Implement the agreed UI and behavior changes in `MainWindow` with a fluent layout, standard vs advanced options, two always-visible creation buttons, and supporting business logic updates (partition/format/driver/diagnostics).

## Current Phase
Phase 2 (Planning complete, waiting for implementation start)

## Phases

### Phase 1: Requirements and Discovery
- [x] Confirm UX direction with user
- [x] Confirm fluent spacing rules (4/8/12/16/24/32/48)
- [x] Identify existing code locations (XAML, ViewModel, WinPE services, resources)
- [x] Capture requirements in `findings.md`
- **Status:** complete

### Phase 2: Detailed Design and Scope Lock
- [x] Define target section structure (Standard, Advanced, Actions)
- [x] Confirm two action buttons remain always visible
- [x] Confirm feature scope for this batch:
- Custom driver folder path
- USB format mode (Quick default, Full optional)
- Explicit partition mode with architecture-aware constraints
- Diagnostics entry under `Tools` menu
- [x] Build implementation plan and file-level impact list
- **Status:** complete

### Phase 3: UI Restructure (MainWindow)
- [ ] Reorganize `Grid.Row="2"` into:
- Standard configuration section
- Collapsible advanced section (closed by default)
- Fixed bottom action bar
- [ ] Keep fluent typography styles and spacing scale usage
- [ ] Keep both `CreateIso` and `CreateUsb` buttons visible at all times
- [ ] Add UI fields for:
- Custom driver folder path + browse action
- USB format mode selector
- Advanced section toggle text/icon
- **Status:** pending

### Phase 4: ViewModel Contract Updates
- [ ] Add new observable properties in `MainWindowViewModel`:
- Advanced panel visibility state
- Custom driver path
- USB format mode selection
- Partition mode selection model (if expanded beyond current enum)
- [ ] Add/adjust commands:
- Toggle advanced options
- Browse custom drivers directory
- Open diagnostics/log location
- Export diagnostics bundle (if included in first pass)
- [ ] Extend `CanExecute` logic and operation-state refresh for new controls
- **Status:** pending

### Phase 5: Domain and Service Logic
- [ ] Extend option contracts:
- `IsoOutputOptions` and `UsbOutputOptions` for custom drivers + USB format mode
- [ ] Update media flow in `MediaOutputService`:
- Merge vendor drivers + custom drivers
- Validate custom path existence/contents
- Apply partition constraints by architecture (ARM64 => GPT/UEFI-safe behavior)
- [ ] Update USB provisioning (`WinPeUsbMediaService`) to support quick/full formatting mode
- [ ] Keep existing validation pipeline and diagnostics model consistent
- **Status:** pending

### Phase 6: Menu and Diagnostics in Tools
- [ ] Add `Tools` top menu in `MainWindow.xaml`
- [ ] Add string resources (`AppStrings.resx`, `AppStrings.en-US.resx`, `AppStrings.fr-FR.resx`)
- [ ] Wire menu actions in ViewModel/shell service:
- Open diagnostics folder
- Export diagnostics/logs
- [ ] Ensure menu placement remains coherent with File/Theme/Language/Help
- **Status:** pending

### Phase 7: Testing, Validation, and Handoff
- [ ] Compile and run smoke checks:
- `dotnet build src/Foundry.slnx`
- ISO flow (validation + command path)
- USB flow (validation + partition/format choices)
- [ ] Validate ARM64 partition restrictions
- [ ] Validate custom driver path behavior on success/failure paths
- [ ] Verify localization keys resolve in EN/FR
- [ ] Final review and handoff notes
- **Status:** pending

## Key Questions
1. For USB full format mode, should both BOOT and CACHE partitions be full format, or CACHE only?
2. In ARM64 mode, do we hide unsupported partition choices or show disabled choices with explanation?
3. Should custom driver path be optional additive input (vendor + custom) or an exclusive override mode?
4. Should diagnostics export produce a single zip, and what retention policy should apply?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Keep both `Create ISO` and `Create USB` buttons always visible | Matches user workflow and avoids mode switching friction |
| Use progressive disclosure (Standard + Advanced) | Cleaner default UI while preserving power features |
| Keep WinPE language and WinPE drivers in Standard section | User confirmed these are essential, not advanced |
| Place diagnostics/log actions in `Tools` menu | Operationally more coherent than `Help` |
| Keep fluent styles and fluent spacing scale only | Visual consistency and maintainability |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| None | 1 | N/A |

## Notes
- Expected implementation touch points:
- `src/Foundry/MainWindow.xaml`
- `src/Foundry/ViewModels/MainWindowViewModel.cs`
- `src/Foundry/Services/ApplicationShell/IApplicationShellService.cs`
- `src/Foundry/Services/ApplicationShell/ApplicationShellService.cs`
- `src/Foundry/Services/WinPe/IsoOutputOptions.cs`
- `src/Foundry/Services/WinPe/UsbOutputOptions.cs`
- `src/Foundry/Services/WinPe/MediaOutputService.cs`
- `src/Foundry/Services/WinPe/WinPeUsbMediaService.cs`
- `src/Foundry/Resources/AppStrings.resx`
- `src/Foundry/Resources/AppStrings.en-US.resx`
- `src/Foundry/Resources/AppStrings.fr-FR.resx`

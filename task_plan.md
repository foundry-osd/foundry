# Task Plan: Foundry UI Refactor (Rufus-Like) + ISO/USB Behavior Updates

## Goal
Refactor the UI and ISO/USB creation behavior to match the requested workflow (ISO picker, simplified options, warning confirmation, Rufus-like layout) without regressing the WinPE pipeline.

## Current Phase
Phase 3 (ready for implementation)

## Phases
### Phase 1: Requirements & Discovery
- [x] Capture all requested UI/UX and behavior changes.
- [x] Map impacted files (XAML, ViewModel, services, resources).
- [x] Identify current technical constraints (USB validation, WinPE options).
- **Status:** complete

### Phase 2: Planning & Architecture
- [x] Define implementation split between UI and domain logic.
- [x] Validate WPF patterns with Context7 (OpenFileDialog, MessageBox, command enablement, horizontal list layout).
- [x] Document required model/option changes (multi-vendor drivers, no confirmation code, automatic drive letter).
- **Status:** complete

### Phase 3: UI Refactor (XAML + style)
- [ ] Remove staging path display from the UI.
- [ ] Replace ISO path input with readonly `TextBox` + `Select...` button (file picker).
- [ ] Replace signature mode ComboBox with `CA2023` checkbox (unchecked default means CA2011).
- [ ] Replace WinPE driver options with two checkboxes: `Dell` and `HP`.
- [ ] Remove obsolete media options (`Include drivers`, `Include preview`).
- [ ] Convert USB selection to a horizontal `ListBox` and keep a USB refresh button.
- [ ] Remove manual USB boot drive letter input.
- [ ] Remove UI confirmation code fields.
- [ ] Move `Create ISO` and `Create USB` buttons to a bottom horizontal action row above the progress area.
- [ ] Remove the border around the main form section (`Grid.Row=1`, previous border around the form).
- [ ] Apply a Rufus-like visual treatment (section headers, compact spacing, contrast hierarchy).
- **Status:** pending

### Phase 4: ViewModel & Services Behavior
- [ ] Add a VM command to open ISO file picker through shell service.
- [ ] Add `CanCreateIso` (disabled until a valid ISO path is selected).
- [ ] Add `CanCreateUsb` (disabled until a USB target is selected).
- [ ] Replace signature enum selector with bool `UseCa2023` and map to `WinPeSignatureMode`.
- [ ] Map `Dell`/`HP` checkboxes to driver resolution rules:
- none selected => `IncludeDrivers=false`
- Dell only => Dell filter
- HP only => HP filter
- Dell + HP => explicit multi-vendor filtering (service/catalog update)
- [ ] Make USB boot drive letter automatic (no user input).
- [ ] Replace confirmation code logic with a warning `MessageBox` Yes/No before USB creation.
- [ ] Extend `IApplicationShellService` for dialogs (ISO picker + warning confirmation).
- [ ] Update localized resources (neutral + en-US + fr-FR) for new labels and removed controls.
- [ ] Remove obsolete code paths introduced by these changes (old VM properties/commands, old service option fields, old USB confirmation flow).
- [ ] Remove obsolete resource keys and UI bindings that are no longer referenced.
- **Status:** pending

### Phase 5: Validation & Delivery
- [ ] Run local build and verify compilation.
- [ ] Verify button states (`Create ISO` / `Create USB`) against prerequisites.
- [ ] Test UI flow: pick ISO, refresh/select USB, warning confirmation.
- [ ] Validate driver scenarios: none, Dell, HP, Dell+HP.
- [ ] Verify desktop and compact-window visual consistency.
- [ ] Run dead-code sweep (`rg`) to confirm removed legacy symbols are not referenced.
- **Status:** pending

## Key Questions
1. For Dell+HP, should filtering be strictly limited to those two vendors (recommended), rather than `Any` (which includes Lenovo/Microsoft)?
2. Should the USB warning confirmation include disk number + friendly name + size to reduce operator risk?
3. Should the Rufus-like styling stay inside current Fluent resources (recommended) or use a dedicated style dictionary?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Keep the existing WinPE pipeline and focus changes on UI/VM + driver filter logic | Minimizes deep regression risk |
| Use WPF `OpenFileDialog` for ISO selection instead of free text input | Reduces path errors and matches requested UX |
| Use warning `MessageBox` Yes/No before USB creation | Replaces typed confirmation while preserving a safety barrier |
| Split command gating into `CanCreateIso` and `CanCreateUsb` | Supports requested independent enablement rules |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
|       | 1       |            |

## Notes
- Current code enforces single-vendor selection and typed USB confirmation code; both require behavior changes.
- Current USB validation requires `TargetDriveLetter`; automation will need a default selection path in service/VM.

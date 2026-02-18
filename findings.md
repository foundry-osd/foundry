# Findings & Decisions

## Requirements
- Do not show staging path in the UI.
- ISO path must use a file picker; `Create ISO` must stay disabled until a valid path is selected.
- Signature mode must default to CA2011 with CA2023 as a checkbox option.
- WinPE drivers must use `Dell` and `HP` checkboxes; if neither is checked, do not inject drivers.
- Remove obsolete media options (`include drivers` and `preview`) from UI.
- USB selection should be displayed in a horizontal list with a refresh USB button.
- Do not ask for USB boot drive letter (automatic assignment).
- Replace typed confirmation code flow with a warning confirmation dialog.
- Place `Create ISO` and `Create USB` buttons horizontally at the bottom, just above the progress bar area.
- `Create USB` must be disabled if no USB key is selected.
- Remove border around main form section (`Grid.Row=1`, previous border around line ~155).
- Update control styling to a Rufus-like layout (based on screenshot).
- Keep all new/updated text in English.
- Remove obsolete code created by the previous UX model once replacement behavior is implemented.

## Research Findings
- Current ViewModel exposes one `CanCreateMedia` boolean used by both `CreateIsoCommand` and `CreateUsbCommand`.
- Current ViewModel properties include:
- `SelectedSignatureMode` (enum selector)
- `SelectedVendor` (single enum vendor)
- `IncludeDrivers` and `IncludePreviewDrivers`
- `UsbBootDriveLetter`
- `UsbConfirmationCode` and `UsbConfirmationCodeRepeat`
- `WinPeUsbMediaService.ValidateDiskSafety` currently requires both confirmation fields to match `ERASE-DISK-{n}`.
- `MediaOutputService.ValidateUsbOptions` currently requires non-empty `TargetDriveLetter`.
- `WinPeVendorSelection` is a single value (`Any`, `Dell`, `Hp`, `Lenovo`, `Microsoft`) and does not natively support Dell+HP multi-selection.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Split command gating into `CanCreateIso` and `CanCreateUsb` | Each action has different prerequisites |
| Add shell service API for ISO picker and warning confirmation | Keeps VM testable and centralizes OS dialogs |
| Replace single vendor selection with Dell/HP booleans | Matches requested UX exactly |
| Implement explicit multi-vendor filtering for Dell+HP | Avoids `Any`, which includes unintended vendors |
| Keep Fluent baseline and add localized Rufus-like style adjustments | Lower implementation risk and faster integration |
| Include explicit obsolete-code cleanup in execution scope | Prevent long-term maintenance debt and stale validation logic |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Current code requires typed USB confirmation code | Planned: remove typed confirmation contract from UI flow and use warning dialog |
| Current code requires user-supplied boot drive letter | Planned: auto-select drive letter in service/VM path |
| Driver filtering supports only one vendor at a time | Planned: update options/catalog filtering for multi-vendor scenario |

## Resources
- `src/Foundry/MainWindow.xaml`
- `src/Foundry/ViewModels/MainWindowViewModel.cs`
- `src/Foundry/Services/WinPe/MediaOutputService.cs`
- `src/Foundry/Services/WinPe/WinPeUsbMediaService.cs`
- `src/Foundry/Services/ApplicationShell/IApplicationShellService.cs`
- `src/Foundry/Resources/AppStrings.en-US.resx`
- `src/Foundry/Resources/AppStrings.fr-FR.resx`
- Context7 WPF reference: https://context7.com/dotnet/wpf/llms.txt
- .NET docs source: https://github.com/dotnet/docs

## Visual/Browser Findings
- The Rufus screenshot uses dark, compact, sectioned layout with strong headings and horizontal separators.
- Primary actions are grouped at the bottom in a clear horizontal row.
- Form density is compact: concise labels, predictable control alignment, low visual noise.
- Destructive workflows rely on explicit warnings/status rather than long typed confirmations.

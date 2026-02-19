# Findings and Decisions

## Requirements
- Reorganize `MainWindow` main content (`Grid.Row="2"`) with clear hierarchy:
- Standard configuration (always visible)
- Advanced options (collapsible, closed by default)
- Action area (always visible)
- Keep both creation buttons always available:
- `CreateIsoCommand`
- `CreateUsbCommand`
- Use fluent text styles and fluent spacing scale only.
- Keep existing foreground brush usage:
- `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`
- Keep WinPE language and WinPE driver vendor selection as standard/essential options.
- Add new options discussed:
- Custom driver folder path (local)
- USB format mode (Quick default, Full optional)
- Explicit partition mode behavior with WinPE ADK context and architecture guardrails
- Add diagnostics/log features under a new top menu `Tools`.

## Research Findings
- Existing command and state wiring already lives in `src/Foundry/ViewModels/MainWindowViewModel.cs`.
- Existing partition style enum is limited to `Mbr` / `Gpt` (`src/Foundry/Services/WinPe/UsbPartitionStyle.cs`).
- USB provisioning currently formats both partitions with `quick` in diskpart script (`src/Foundry/Services/WinPe/WinPeUsbMediaService.cs`).
- Media validation already exists in `MediaOutputService.ValidateIsoOptions` and `ValidateUsbOptions`.
- Driver injection pipeline already exists and can accept driver directories (`WinPeDriverInjectionOptions.DriverPackagePaths`), which is a good extension point for custom local drivers.
- Current shell service supports ISO file picker and confirmation dialogs but not folder picker/export helper yet (`IApplicationShellService` / `ApplicationShellService`).
- Menus currently include `File`, `Theme`, `Language`, `Help`; no `Tools` menu yet (`src/Foundry/MainWindow.xaml`).
- Resource strings exist in three files:
- `src/Foundry/Resources/AppStrings.resx`
- `src/Foundry/Resources/AppStrings.en-US.resx`
- `src/Foundry/Resources/AppStrings.fr-FR.resx`

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Keep two create buttons always visible | User preference and faster expert workflow |
| Introduce collapsible advanced section | Reduces visual noise while preserving depth |
| Keep language + vendor drivers in standard section | User-defined essential inputs |
| Add diagnostics in `Tools` | Better functional grouping than `Help` |
| Reuse existing WinPE validation/diagnostic flow | Lower risk and consistent behavior |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| None during planning | N/A |

## Resources
- `src/Foundry/MainWindow.xaml`
- `src/Foundry/ViewModels/MainWindowViewModel.cs`
- `src/Foundry/Services/WinPe/MediaOutputService.cs`
- `src/Foundry/Services/WinPe/WinPeUsbMediaService.cs`
- `src/Foundry/Services/ApplicationShell/IApplicationShellService.cs`
- `src/Foundry/Services/ApplicationShell/ApplicationShellService.cs`
- `src/Foundry/Resources/AppStrings.resx`
- `src/Foundry/Resources/AppStrings.en-US.resx`
- `src/Foundry/Resources/AppStrings.fr-FR.resx`

## Visual/Browser Findings
- User references Rufus layout style:
- Prominent section headers
- Dense but readable form groupings
- Advanced options folded below core controls
- Persistent bottom action buttons
- This supports adopting a progressive disclosure layout while keeping operations immediately available.

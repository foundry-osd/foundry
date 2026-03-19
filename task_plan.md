# Task Plan: Foundry Window Standard and Expert Mode Architecture

## Goal
Define the product and UI architecture for a Foundry window that supports `Simple/Standard` and `Expert` modes, advanced grouped settings, import/export flows, and a modular configuration model that can later drive `foundry deploy`.

## Current Phase
Phase 2

## Phases
### Phase 1: Requirements & Discovery
- [x] Understand user intent
- [x] Identify constraints and requirements
- [x] Inspect current Foundry window implementation
- [x] Document findings in findings.md
- [x] Collect open product questions for the user
- **Status:** complete

### Phase 2: Product Structure Proposal
- [x] Define mode-switch behavior
- [x] Define advanced navigation/grouping model
- [x] Define parent/child option enablement rules
- [x] Define import/export and config file strategy
- [x] Resolve remaining behavior gaps and inconsistencies
- [x] Produce the target UX architecture for Standard and Expert
- **Status:** complete

### Phase 3: Technical Direction
- [x] Define the canonical config model and expert-only sections
- [x] Define how Standard maps onto the canonical config model
- [x] Define the generated deploy-consumable contract
- [x] Identify extensibility points for future advanced options
- [x] Identify persistence/model constraints
- **Status:** complete

### Phase 4: UI and Integration Mapping
- [x] Map the proposal onto `MainWindow.xaml` and `MainWindowViewModel.cs`
- [x] Decide what should become reusable view models or grouped section models
- [x] Define how generated config reaches `X:\Foundry\Config\foundry.deploy.config.json`
- [x] Capture any dependencies on `Foundry.Deploy`
- **Status:** complete

### Phase 5: Validation and Delivery
- [ ] Verify the proposal against current implementation constraints
- [ ] Capture unresolved decisions
- [ ] Prepare implementation-ready next steps
- [ ] Deliver the updated architecture summary to the user
- **Status:** in_progress

## Key Questions
1. What is the exact startup/application order for catalog-dependent expert config values inside `Foundry.Deploy`?
2. What should the embedded Expert localization registry schema look like in its first version?
3. Which future Expert groups should be scheduled first after `MachineNaming`?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Use the `planning-with-files` workflow for this turn | The task is multi-step and discovery-heavy |
| Start with discovery and requirements questions before implementation | The user explicitly asked to brainstorm and clarify needs first |
| Avoid proposing subprojects or test projects | Explicit user constraint |
| Use Context7 against official WPF docs for framework grounding | Explicit user request to use Context7 |
| Base the future expert mode on a stable configuration contract rather than the current flat window state | Needed for import/export and deploy compatibility |
| Treat `Simple` and `Standard` as the same mode | User decision |
| Keep the current Standard values visible in Expert under a first `General` category | User decision |
| Put mode switching in a new top-menu `Mode` menu | Best fit for an app-wide shell change and accepted by the user |
| Use File menu actions for import/export | User decision |
| Keep full expert config export separate from deploy-consumable export | User decision |
| Auto-generate `X:\\Foundry\\Config\\foundry.deploy.config.json` from Expert selections | User decision refined by codebase path conventions |
| Do not inject `foundry.deploy.config.json` when building media in Standard mode | Standard mode should ignore expert-only settings and fresh media builds recreate the boot image from scratch |
| `Foundry.Deploy` should auto-load the generated config silently, without any UI banner | User decision; keep startup friction-free and rely on logging instead of visible notification |
| Keep expert values in memory when switching back to Standard | User decision |
| Ignore unknown JSON fields on import, while validating known required values | Best forward-compatible behavior per official .NET docs |
| Use a built-in curated language list for Expert localization, then validate/intersect it against deploy reality | Stable UX without binding the editor directly to live catalog data |
| Strip disabled child values from exported JSON but preserve them in memory | Clean export and better editing UX |
| Load the generated deploy config in `Foundry.Deploy` through a dedicated optional loader service rather than `Program.cs` | Keeps bootstrap clean and puts mapping logic near deploy state |
| Store the Expert localization registry as an embedded JSON resource in `Foundry` | Product data should stay deterministic, offline, and easier to maintain than code constants |
| Persist only `MachineNaming` in `Customization` for v1, with `Autopilot`, `APPX removal`, and `Custom deploy config` as UI-only placeholders | Avoids premature schema expansion while still showing the product direction |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `rg.exe` could not start with access denied in this environment | 1 | Switched to PowerShell-based file search |

## Notes
- Keep the solution modular so new expert sections/options can be added without reworking the window shell.
- Keep proposals compatible with the existing top menu the user wants to preserve.
- The implementation-ready draft now exists; remaining work is validation against actual code edits and final implementation sequencing.

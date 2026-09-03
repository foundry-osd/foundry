# Machine Name Composition Design

## Summary

Replace Foundry's prefix and generated-suffix options with a structured machine-name composition editor. Users build an ordered name from supported component types without writing template variables. Foundry validates the complete composition before media creation, resolves hardware-backed values in Foundry Deploy, and always produces a Windows-compatible name of at most 15 characters.

The change preserves manual naming, adds enterprise hardware identity sources, supports deterministic left- or right-side truncation per component, and keeps all machine identifiers out of telemetry and logs.

## Goals

- Give users flexible, control-based machine-name composition.
- Support static text, serial number, manufacturer, model, asset tag, SMBIOS UUID, and random characters.
- Allow each component type at most once and allow components to be reordered.
- Make the 15-character Windows limit explicit and deterministic during authoring.
- Preserve manual naming as an alternative to composed naming.
- Preserve existing configurations through migration to the new contract.
- Keep authoring, generated Deploy configuration, and Deploy runtime validation aligned.
- Follow existing Foundry OSD WinUI 3 layout, typography, spacing, control, accessibility, and localization conventions.
- Update telemetry properties so they describe the new structure without collecting identifiers or generated names.

## Non-goals

- A text-based template or variable language.
- Conditional rules based on manufacturer, model, or other hardware values.
- MAC addresses, disk identifiers, dates, times, architecture, or the existing ambiguous product/version value as naming components.
- Fleet-wide uniqueness detection. Foundry can generate deterministic names but cannot detect a collision without a central inventory service.
- Changing Windows computer-name character rules beyond the existing ASCII letter, digit, and hyphen contract.
- Supporting underscore as a separator because it is not valid in DNS host names.
- Changing enterprise application authentication or Autopilot connection behavior.

## User Experience

### Modes

Machine-name customization retains the page-level enabled state and offers two modes when enabled:

- **Manual:** the technician enters the complete computer name during deployment. An optional initial value may prefill the field without constraining the technician's edits.
- **Composed:** Foundry resolves an ordered collection of configured components.

Composed mode offers **Allow editing during deployment**. When enabled, Foundry Deploy prefills the complete generated name and lets the technician edit it. When disabled, the generated name is read-only. Final validation applies in both cases.

### Component builder

The builder contains an ordered list. Each row provides:

- the component type;
- controls applicable to that type;
- accessible move-up and move-down actions;
- a remove action.

An **Add component** action offers only types that are not already present. Every component type may appear at most once, including static text and random characters.

Supported types and controls:

| Type | Value source | Controls | Default truncation |
| --- | --- | --- | --- |
| Static text | User-entered text | Text | Not applicable |
| Serial number | `Win32_BIOS.SerialNumber` | Maximum length, keep side | Keep right |
| Manufacturer | `Win32_ComputerSystem.Manufacturer` | Maximum length, keep side | Keep left |
| Model | `Win32_ComputerSystem.Model` | Maximum length, keep side | Keep left |
| Asset tag | `Win32_SystemEnclosure.SMBIOSAssetTag` | Maximum length, keep side | Keep left |
| SMBIOS UUID | `Win32_ComputerSystemProduct.UUID` | Maximum length, keep side | Keep left |
| Random characters | Cryptographically secure uppercase alphanumeric value | Length | Not applicable |

**Keep left** retains the first configured number of sanitized characters. **Keep right** retains the last configured number. If the value is shorter than the configured maximum, Foundry uses the complete value without padding. Serial numbers therefore keep their rightmost 15 characters when used alone and their rightmost remaining allocation when combined with other components.

### Global composition options

- **Separator:** None or hyphen. The separator is inserted between every resolved, non-empty component.
- **Casing:** Preserve, Uppercase, or Lowercase. Casing is applied after component sanitization and before final validation. Preserve is the compatibility default.
- **Character budget:** Static text uses its sanitized length. Dynamic components use their configured maximum length. Separators use `component count - 1` characters. The configuration cannot be saved or used to create media when the maximum total exceeds 15.

### Preview and validation

Foundry OSD shows a representative preview, the configured maximum length, and the remaining character budget. Preview values are clearly examples because target hardware is not available during authoring.

Validation is immediate and localized. It covers:

- at least one component in composed mode;
- unique component types;
- non-empty valid static text;
- valid dynamic lengths;
- a maximum configured result of 15 characters;
- valid enum and component values;
- a final name containing only ASCII letters, digits, or hyphens.

Foundry Deploy additionally validates that every configured hardware value is available and usable. Blank values and known firmware placeholders, including `Unknown`, `Default string`, and `To Be Filled By O.E.M.`, block deployment with a localized explanation. Foundry never silently omits the component or substitutes a random value.

## WinUI 3 Design Requirements

The Machine Naming page must remain visually consistent with the other Foundry OSD configuration pages:

- retain `PageHeader`, `ScrollView`, the established content margins, and the existing page enable toggle;
- use CommunityToolkit `SettingsCard` and `SettingsExpander` patterns already present in Foundry;
- use standard WinUI 3 `ComboBox`, `NumberBox`, `TextBox`, `ToggleSwitch`, and `Button` controls;
- use existing Foundry theme resources for page, section, group, and inline-control spacing;
- use existing typography resources and avoid custom font sizes when a semantic style exists;
- keep state and enablement rules in the view model;
- use compiled bindings where practical and follow existing binding conventions;
- support narrow windows, text scaling, wrapped translated strings, keyboard navigation, visible focus, and screen readers;
- provide automation names for icon-only move and remove actions;
- expose reordering through buttons rather than drag-and-drop alone;
- display localized validation adjacent to the affected control and summarize composition-level errors near the preview.

Implementation review must compare the page with General Configuration, OOBE, Ethernet 802.1X, Wi-Fi, and Autopilot pages before the UI is considered complete.

## Configuration Contracts

### Shared authoring model

`Foundry.Core` owns the persisted authoring contract and shared enums. The design introduces concepts equivalent to:

- `MachineNamingMode`: Manual or Composed;
- `MachineNameComponentType`: StaticText, SerialNumber, Manufacturer, Model, AssetTag, SystemUuid, or Random;
- `MachineNameTruncation`: KeepLeft or KeepRight;
- `MachineNameSeparator`: None or Hyphen;
- `MachineNameCasing`: Preserve, Uppercase, or Lowercase;
- `MachineNameComponentSettings`: type, static text when applicable, maximum length when applicable, and truncation when applicable;
- revised `MachineNamingSettings`: enabled state, mode, optional manual initial value, ordered components, separator, casing, and deployment-edit permission.

Names may be adjusted during implementation to match existing conventions, but the persisted semantics must remain explicit and strongly typed.

### Deploy contract

The generated Deploy configuration mirrors only the fields required at runtime. The generator validates the authoring model before writing it. `Foundry.Core` and `Foundry.Deploy` retain separate schema models as required by repository architecture, while sharing composition rules from `Foundry.Core`.

### Schema versions and migration

The latest published release baseline is Foundry schema 13 and Deploy schema 11. This contract change increments them to Foundry schema 14 and Deploy schema 12. Connect remains unchanged.

When older Foundry configuration is loaded:

- disabled naming remains disabled;
- an enabled generated suffix becomes a composed configuration containing the existing prefix as static text when present and a six-character random component, with no separator;
- the previous suffix-edit setting becomes complete-name deployment editing;
- an enabled manual configuration becomes Manual mode and uses the previous prefix as its initial value.

The migration preserves the generated or prefilled value and whether technician editing was available. The approved complete-name editing model intentionally replaces prefix-only editing and enforcement.

New Deploy runtime code accepts older Deploy configurations through a focused legacy adapter. The new generated schema contains only the structured contract. Compatibility members remain only where deserialization requires them and are excluded from newly generated JSON.

## Shared Composition Engine

`Foundry.Core` owns a pure, framework-independent composition service and validation rules. The service accepts:

- structured settings;
- a value set keyed by component type;
- a supplied random value for deterministic testing and stable runtime generation.

For each ordered component, it:

1. obtains static or resolved input;
2. rejects unavailable or placeholder hardware input;
3. removes characters outside the existing computer-name character set;
4. rejects a value that becomes empty after sanitization;
5. applies the component's maximum length from the configured side;
6. joins results with the selected separator;
7. applies global casing;
8. validates the final 1-to-15-character name.

The result distinguishes configuration errors, missing hardware values, and invalid final output so each caller can show an appropriate localized message.

Random data is generated once per deployment preparation context and remains stable while the user moves through the wizard or edits the generated name.

## Hardware Discovery

`Foundry.Utilities` extends its existing Windows hardware inspection output with:

- SMBIOS asset tag from `Win32_SystemEnclosure.SMBIOSAssetTag`;
- SMBIOS UUID from `Win32_ComputerSystemProduct.UUID`.

These values flow through `HardwareSnapshot`, the Deploy hardware profile service, and `HardwareProfile`. Placeholder detection is centralized so authoring preview, runtime composition, diagnostics, and tests do not grow inconsistent copies.

Hardware identifiers remain sensitive. They must not be written to telemetry, included in ordinary informational logs, or exposed in support bundles without the existing sanitization policy.

## Runtime Flow

Deployment startup already loads configuration and hardware before applying the startup snapshot. The wizard must apply hardware-backed naming atomically, either by passing the loaded hardware profile with the naming configuration or by resolving only after both are present. It must not briefly expose an invalid generated name before hardware arrives.

In Manual mode, the target step retains the normal editable computer-name field.

In Composed mode, the target step receives the composition result and either locks or enables the complete field according to configuration. Missing-value and composition errors make deployment unready and display a localized explanation. Final launch preparation validates the normalized complete name; obsolete prefix/suffix-specific launch requirements are removed.

## Telemetry

Boot-media telemetry is updated to reflect the structured configuration. Safe structural properties may include:

- whether machine naming is enabled;
- Manual or Composed mode;
- component count;
- ordered component types;
- separator selection;
- casing selection;
- per-component truncation direction;
- whether deployment editing is enabled.

Telemetry must not include:

- static text;
- configured maximum lengths;
- generated or edited computer names;
- serial numbers;
- manufacturer or model values resolved from a target;
- asset tags;
- SMBIOS UUIDs;
- random output;
- any raw component value.

The telemetry allowlist, property builder, privacy tests, and obsolete machine-naming properties are updated together. `customization_machine_naming_prefix_configured` is removed because prefix is no longer the governing model. Existing identifier-deny rules remain in force.

## Localization

Every new or changed Foundry OSD and Foundry Deploy string is translated in every locale currently shipped by the respective application. This includes:

- mode, component, separator, casing, and truncation labels;
- builder actions and automation names;
- preview and character-budget text;
- validation and missing-hardware messages;
- complete-name deployment editing text;
- deployment target-state explanations.

English resource keys remain the source contract. Localization validation must confirm key parity across all locale files.

## Testing Strategy

Tests are limited to behavior with clear business value.

### Foundry.Core.Tests

- ordered composition and separators;
- one-instance-per-type validation;
- sanitization and final validation;
- global casing;
- left- and right-side truncation, including rightmost serial behavior;
- configured character-budget calculation;
- unavailable and placeholder value results;
- legacy authoring migration;
- authoring-to-Deploy generation and schema expectations;
- configuration overview readiness.

### Foundry.Utilities.Tests

- asset tag and SMBIOS UUID parsing and trimming;
- absent hardware values.

### Foundry.Deploy.Tests

- legacy Deploy configuration adaptation;
- naming resolution from the startup hardware snapshot;
- stable random generation within one preparation context;
- blocking behavior for unavailable hardware values;
- complete-name lock and edit behavior;
- final launch validation without obsolete prefix/suffix rules.

### Foundry.Telemetry.Tests

- new structural properties are allowlisted and emitted;
- obsolete prefix properties are absent;
- static text, resolved values, computer names, and hardware identifiers are never emitted.

No tests are added for WinUI or WPF framework behavior, bindings, trivial properties, or behavior already covered at a lower layer.

## Cleanup

After behavior is green:

- remove the duplicated Deploy `ComputerNameRules` implementation and use the Core rules;
- remove obsolete prefix/suffix helpers, view-model properties, launch-request fields, resources, and tests replaced by composition behavior;
- retain only narrowly scoped legacy deserialization or migration code required for compatibility;
- remove dead aliases and adapters once all consumers use the shared engine;
- avoid unrelated refactoring.

## Documentation and Delivery

Code work occurs on `feat/machine-name-composition` in its dedicated worktree. Commits remain atomic and use English Conventional Commit messages. The branch is pushed and a code pull request is opened without squashing.

Documentation work occurs in a separate GitBook worktree and branch. It updates:

- `foundry-osd/customization/machine-naming.md` with the builder, component sources, sanitization, character budgeting, examples, missing-value behavior, editing, and compatibility notes;
- `foundry-deploy/target.md` with composed-name resolution, locked/editable behavior, and blocking errors.

The GitBook branch is pushed and its own pull request is opened without squashing. The public `docs.foundryosd.com/~/revisions/...` preview URL is retrieved from the GitBook status check and returned with both PR links. Worktrees and branches remain available for review.

Before either PR is opened, run the repository-required formatting and validation commands. For Foundry, this includes `scripts/Test-FoundryFormat.ps1`, focused tests, the full applicable x64 test suite, and the Release build. Local ARM64 validation is required only if implementation introduces architecture-sensitive behavior; x64 and ARM64 CI remain blocking after the PR opens.

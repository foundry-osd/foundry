# Machine Name Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace prefix-based machine naming with a localized, structured WinUI 3 composition editor and deterministic hardware-backed name generation in Foundry Deploy.

**Architecture:** `Foundry.Core` owns strongly typed settings, validation, migration, and a pure composition engine. Foundry OSD edits ordered components through standard WinUI controls, while Foundry Deploy maps its separate runtime schema and already-collected startup hardware into the shared engine exactly once. Telemetry records only structural choices; sensitive values never leave the device.

**Tech Stack:** C# 14, .NET 10, WinUI 3, WPF, CommunityToolkit.WinUI Controls, CommunityToolkit.Mvvm, System.Text.Json, xUnit v3, PowerShell, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-09-03-machine-name-composition-design.md`

## Global Constraints

- Work only in `E:\Github\Foundry Project\worktrees\machine-name-composition` on `feat/machine-name-composition` for product code.
- Keep Foundry authoring and Foundry Deploy runtime schemas separate; Connect schema stays at 3.
- Set Foundry schema to 14 and Deploy schema to 12, based on published release `v26.9.1.1`.
- Each component type appears at most once; separators are None or Hyphen; final names are 1-15 ASCII letters, digits, or hyphens.
- Serial number defaults to KeepRight. Other hardware components default to KeepLeft.
- Missing, blank, sanitized-empty, or placeholder hardware values block deployment.
- Use only standard WinUI 3 controls and existing Foundry typography, width, margin, and spacing resources.
- Translate every new or changed string in all existing Foundry and Foundry Deploy locales.
- Do not add UI-framework, binding, trivial-property, or duplicated tests.
- Do not add static text, maximum lengths, generated names, serials, asset tags, UUIDs, random output, or resolved hardware values to telemetry.
- Preserve existing coarse vendor/model telemetry; it is outside this change.
- Remove obsolete prefix/suffix code and duplicated rules only after replacements are green.
- Main agent owns all edits, commits, pushes, and PRs. Subagents are read-only reviewers because `AGENTS.md` forbids delegated edits.

---

## File Structure

New Core contracts live in `src/Foundry.Core/Models/Configuration/`. Validation, placeholder policy, and composition live in `src/Foundry.Core/Services/Configuration/`. Deploy keeps its schema records under `src/Foundry.Deploy/Models/Configuration/` and maps them at its boundary. One partial Foundry OSD view-model file owns machine-naming page state, and one row view model owns per-component presentation.

Primary new files:

- `src/Foundry.Core/Models/Configuration/MachineNamingMode.cs`
- `src/Foundry.Core/Models/Configuration/MachineNameComponentType.cs`
- `src/Foundry.Core/Models/Configuration/MachineNameTruncation.cs`
- `src/Foundry.Core/Models/Configuration/MachineNameSeparator.cs`
- `src/Foundry.Core/Models/Configuration/MachineNameCasing.cs`
- `src/Foundry.Core/Models/Configuration/MachineNameComponentSettings.cs`
- `src/Foundry.Core/Services/Configuration/MachineNamingValidator.cs`
- `src/Foundry.Core/Services/Configuration/MachineNameComposer.cs`
- `src/Foundry.Core/Services/Configuration/MachineNameHardwareValueRules.cs`
- `src/Foundry.Deploy/Models/Configuration/DeployMachineNameComponentSettings.cs`
- `src/Foundry.Deploy/Services/Configuration/DeployMachineNamingLegacyAdapter.cs`
- `src/Foundry.Deploy/Services/System/MachineNamePreparationService.cs`
- `src/Foundry/ViewModels/CustomizationConfigurationViewModel.MachineNaming.cs`
- `src/Foundry/ViewModels/MachineNameComponentRowViewModel.cs`

---

### Task 1: Add structured contracts and schema migration

**Files:**
- Create: `src/Foundry.Core/Models/Configuration/MachineNamingMode.cs`
- Create: `src/Foundry.Core/Models/Configuration/MachineNameComponentType.cs`
- Create: `src/Foundry.Core/Models/Configuration/MachineNameTruncation.cs`
- Create: `src/Foundry.Core/Models/Configuration/MachineNameSeparator.cs`
- Create: `src/Foundry.Core/Models/Configuration/MachineNameCasing.cs`
- Create: `src/Foundry.Core/Models/Configuration/MachineNameComponentSettings.cs`
- Modify: `src/Foundry.Core/Models/Configuration/MachineNamingSettings.cs`
- Modify: `src/Foundry.Core/Models/Configuration/ConfigurationSchemaVersions.cs`
- Modify: `src/Foundry.Core/Services/Configuration/FoundryConfigurationMigration.cs`
- Modify: `src/Foundry.Core/Services/Configuration/FoundryConfigurationService.cs`
- Test: `src/Foundry.Core.Tests/Configuration/ConfigurationSchemaVersionsTests.cs`
- Test: `src/Foundry.Core.Tests/Configuration/FoundryConfigurationServiceTests.cs`

**Interfaces:**
- Produces: `MachineNamingSettings`, `MachineNameComponentSettings`, and the five shared enums consumed by every later task.
- Produces: `FoundryConfigurationMigration.ApplySchemaMigrations(FoundryConfigurationDocument)`.

- [ ] **Step 1: Write failing schema and migration tests**

Add assertions equivalent to:

```csharp
Assert.Equal(14, ConfigurationSchemaVersions.FoundryCurrent);
Assert.Equal(12, ConfigurationSchemaVersions.DeployCurrent);
Assert.Equal(3, ConfigurationSchemaVersions.ConnectCurrent);

MachineNamingSettings migrated = loaded.Customization.MachineNaming;
Assert.Equal(MachineNamingMode.Composed, migrated.Mode);
Assert.Collection(migrated.Components,
    component => Assert.Equal(MachineNameComponentType.StaticText, component.Type),
    component =>
    {
        Assert.Equal(MachineNameComponentType.Random, component.Type);
        Assert.Equal(6, component.MaximumLength);
    });
Assert.Equal(MachineNameSeparator.None, migrated.Separator);
Assert.Equal(MachineNameCasing.Preserve, migrated.Casing);
```

Cover schema-13 generated naming with and without prefix, schema-13 manual naming with initial prefix, disabled naming, edit-state migration, and serialization that omits `prefix`, `autoGenerateName`, and `allowManualSuffixEdit` after migration.

- [ ] **Step 2: Run the focused tests and confirm RED**

```powershell
dotnet run --project src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: compile failure because the structured types and schema values do not exist.

- [ ] **Step 3: Add the contracts and schema-aware migration**

Implement these public shapes with XML documentation:

```csharp
public enum MachineNamingMode { Manual, Composed }
public enum MachineNameComponentType { StaticText, SerialNumber, Manufacturer, Model, AssetTag, SystemUuid, Random }
public enum MachineNameTruncation { KeepLeft, KeepRight }
public enum MachineNameSeparator { None, Hyphen }
public enum MachineNameCasing { Preserve, Uppercase, Lowercase }

public sealed record MachineNameComponentSettings
{
    public MachineNameComponentType Type { get; init; }
    public string? StaticText { get; init; }
    public int? MaximumLength { get; init; }
    public MachineNameTruncation? Truncation { get; init; }
}

public sealed record MachineNamingSettings
{
    public bool IsEnabled { get; init; }
    public MachineNamingMode Mode { get; init; } = MachineNamingMode.Manual;
    public string? ManualInitialValue { get; init; }
    public IReadOnlyList<MachineNameComponentSettings> Components { get; init; } = [];
    public MachineNameSeparator Separator { get; init; }
    public MachineNameCasing Casing { get; init; }
    public bool AllowEditingDuringDeployment { get; init; } = true;
}
```

Capture legacy JSON values without writing them back. `FoundryConfigurationService` must pass the source schema to migration before stamping schema 14. Map legacy generated mode to optional StaticText plus Random(6), and legacy manual mode to Manual with `ManualInitialValue=Prefix`.

- [ ] **Step 4: Build and rerun the focused tests**

```powershell
dotnet build src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 5: Commit the contracts**

```powershell
git add src/Foundry.Core src/Foundry.Core.Tests
git commit -m "feat(configuration): add machine name composition contracts"
```

---

### Task 2: Implement validation and deterministic composition

**Files:**
- Modify: `src/Foundry.Core/Services/Configuration/ComputerNameRules.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNamingValidationCode.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNamingValidationIssue.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNamingValidationResult.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNamingValidator.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNameHardwareValueRules.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNameCompositionRequest.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNameCompositionResult.cs`
- Create: `src/Foundry.Core/Services/Configuration/MachineNameComposer.cs`
- Test: `src/Foundry.Core.Tests/Configuration/MachineNamingValidatorTests.cs`
- Test: `src/Foundry.Core.Tests/Configuration/MachineNameComposerTests.cs`

**Interfaces:**
- Consumes: structured contracts from Task 1.
- Produces: `ComputerNameRules.Sanitize(string?)`, `MachineNamingValidator.Validate(MachineNamingSettings)`, and `MachineNameComposer.Compose(MachineNameCompositionRequest)`.

- [ ] **Step 1: Write failing validator tests**

Cover disabled validity, manual initial-name validation, missing components, duplicate types, irrelevant properties, static text that sanitizes empty, 1-15 component lengths, required hardware truncation, random without truncation, separator budget, and total budget above 15.

```csharp
MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);
Assert.Contains(result.Issues, issue =>
    issue.Code == MachineNamingValidationCode.CharacterBudgetExceeded);
Assert.Equal(16, result.MaximumLength);
```

- [ ] **Step 2: Run validator tests and confirm RED**

```powershell
dotnet build src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64
```

Expected: compile failure because validator types do not exist.

- [ ] **Step 3: Implement validation and unbounded sanitization**

`ComputerNameRules.Sanitize` removes unsupported characters without truncating. Existing `Normalize` becomes a compatibility wrapper that truncates `Sanitize(value)` to 15. `MachineNamingValidator` returns all issues with optional component indexes and calculates:

```csharp
int separatorLength = settings.Separator == MachineNameSeparator.Hyphen
    ? Math.Max(0, settings.Components.Count - 1)
    : 0;
int maximumLength = componentLengths.Sum() + separatorLength;
```

- [ ] **Step 4: Write failing composer tests**

Cover ordered joining, no separator, hyphen separator, sanitization before truncation, casing, no padding, random validation, missing values, placeholders, sanitized-empty values, and invalid final names. Include the required serial behavior:

```csharp
MachineNameCompositionResult result = MachineNameComposer.Compose(new()
{
    Components = [new() { Type = MachineNameComponentType.SerialNumber, MaximumLength = 15, Truncation = MachineNameTruncation.KeepRight }],
    Values = new Dictionary<MachineNameComponentType, string?> { [MachineNameComponentType.SerialNumber] = "SERIAL-123456789012345" },
    RandomValue = string.Empty,
});
Assert.Equal("123456789012345", result.ComputerName);
```

- [ ] **Step 5: Implement the composer minimally**

Expose a result with `ComputerName`, `FailureKind`, optional `ComponentType`, optional validation result, and `IsSuccess`. The composer must:

```csharp
string sanitized = ComputerNameRules.Sanitize(rawValue);
string cropped = truncation == MachineNameTruncation.KeepRight
    ? sanitized[^Math.Min(limit, sanitized.Length)..]
    : sanitized[..Math.Min(limit, sanitized.Length)];
```

Use an ordinal-ignore-case placeholder set containing blank, `Unknown`, `Default string`, and `To Be Filled By O.E.M.` plus all-zero/all-`F` UUIDs. Join, apply invariant casing, and validate the final name.

- [ ] **Step 6: Run focused Core tests**

```powershell
dotnet build src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 7: Commit the engine**

```powershell
git add src/Foundry.Core src/Foundry.Core.Tests
git commit -m "feat(configuration): compose machine names from structured components"
```

---

### Task 3: Generate runtime configuration and update overview and telemetry

**Files:**
- Modify: `src/Foundry.Core/Models/Configuration/Deploy/DeployMachineNamingSettings.cs`
- Create: `src/Foundry.Core/Models/Configuration/Deploy/DeployMachineNameComponentSettings.cs`
- Modify: `src/Foundry.Core/Services/Configuration/DeployConfigurationGenerator.cs`
- Modify: `src/Foundry.Core/Services/Configuration/ConfigurationOverviewEvaluator.cs`
- Modify: `src/Foundry.Core/Services/Telemetry/BootMediaTelemetryPropertyBuilder.cs`
- Modify: `src/Foundry.Telemetry/TelemetryEventPropertyPolicy.cs`
- Test: `src/Foundry.Core.Tests/Configuration/DeployConfigurationGeneratorTests.cs`
- Test: `src/Foundry.Core.Tests/Configuration/ConfigurationOverviewEvaluatorTests.cs`
- Test: `src/Foundry.Core.Tests/BootMediaTelemetryPropertyBuilderTests.cs`
- Test: `src/Foundry.Telemetry.Tests/TelemetryEventPropertyPolicyTests.cs`

**Interfaces:**
- Consumes: Task 1 settings and Task 2 validator.
- Produces: schema-12 runtime JSON and privacy-safe structural telemetry.

- [ ] **Step 1: Write failing generator and overview tests**

Assert exact manual/composed/disabled mappings, generator rejection of invalid compositions, schema 12, and absence of legacy properties.

```csharp
Assert.Equal(MachineNamingMode.Composed, generated.Customization.MachineNaming.Mode);
Assert.DoesNotContain("autoGenerateName", json, StringComparison.OrdinalIgnoreCase);
Assert.Equal(ConfigurationOverviewState.NeedsAttention, invalidOverview[ConfigurationOverviewItem.MachineNaming]);
```

- [ ] **Step 2: Implement separate Deploy contract mapping**

Mirror the structured fields with a separate `DeployMachineNameComponentSettings` record. Validate before mapping; disabled naming emits clean defaults. Do not reuse the top-level authoring record as the runtime schema.

- [ ] **Step 3: Write failing telemetry tests**

Assert structural output only:

```csharp
Assert.Equal("composed", properties["customization_machine_naming_mode"]);
Assert.Equal(3, properties["customization_machine_naming_component_count"]);
Assert.False(properties.ContainsKey("customization_machine_naming_prefix_configured"));
Assert.DoesNotContain(properties, pair => pair.Key.Contains("static_text", StringComparison.OrdinalIgnoreCase));
```

Exercise the policy with candidate serial, asset-tag, UUID, computer-name, and static-text keys and assert they are rejected.

- [ ] **Step 4: Implement telemetry and allowlist changes**

Emit stable lowercase structural values for mode, ordered component types, separator, casing, truncation directions, component count, and editing enabled. Do not emit component values or lengths. Add defensive deny tokens for `asset_tag`, `smbios_uuid`, and `system_uuid`; preserve existing coarse vendor/model telemetry.

- [ ] **Step 5: Run focused tests**

```powershell
dotnet build src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64
dotnet build src/Foundry.Telemetry.Tests/Foundry.Telemetry.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Core.Tests/Foundry.Core.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet run --project src/Foundry.Telemetry.Tests/Foundry.Telemetry.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 6: Commit generator and telemetry behavior**

```powershell
git add src/Foundry.Core src/Foundry.Core.Tests src/Foundry.Telemetry src/Foundry.Telemetry.Tests
git commit -m "feat(telemetry): describe machine name composition settings"
```

---

### Task 4: Collect asset tag and SMBIOS UUID

**Files:**
- Modify: `src/Foundry.Utilities/Hardware/HardwareSnapshot.cs`
- Modify: `src/Foundry.Utilities/Hardware/WindowsHardwareInspector.cs`
- Modify: `src/Foundry.Utilities.Tests/Hardware/WindowsHardwareInspectorTests.cs`
- Modify: `src/Foundry.Deploy/Models/HardwareProfile.cs`
- Modify: `src/Foundry.Deploy/Services/Hardware/HardwareProfileService.cs`
- Test: `src/Foundry.Deploy.Tests/HardwareProfileServiceTests.cs`

**Interfaces:**
- Produces: trimmed `AssetTag` and `SystemUuid` values in `HardwareSnapshot` and `HardwareProfile`.

- [ ] **Step 1: Extend tests first**

Update representative inspector JSON and service snapshots:

```csharp
Assert.Equal("ASSET-42", snapshot.AssetTag);
Assert.Equal("4C4C4544-0038-4A10-8058-B6C04F4D5033", snapshot.SystemUuid);
Assert.Equal(string.Empty, missingSnapshot.AssetTag);
Assert.Equal(string.Empty, missingSnapshot.SystemUuid);
```

- [ ] **Step 2: Run hardware tests and confirm RED**

```powershell
dotnet build src/Foundry.Utilities.Tests/Foundry.Utilities.Tests.csproj -c Release -p:Platform=x64
```

Expected: compile failure because the properties do not exist.

- [ ] **Step 3: Extend inspection and mapping**

Update the PowerShell hardware payload to query `Win32_SystemEnclosure` and add:

```powershell
AssetTag = [string]$enclosure.SMBIOSAssetTag
SystemUuid = [string]$product.UUID
```

Trim values when parsing and map them without adding them to display labels, telemetry, or ordinary informational logs.

- [ ] **Step 4: Run focused tests**

```powershell
dotnet build src/Foundry.Utilities.Tests/Foundry.Utilities.Tests.csproj -c Release -p:Platform=x64
dotnet build src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Utilities.Tests/Foundry.Utilities.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet run --project src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 5: Commit hardware discovery**

```powershell
git add src/Foundry.Utilities src/Foundry.Utilities.Tests src/Foundry.Deploy src/Foundry.Deploy.Tests
git commit -m "feat(hardware): collect machine naming identifiers"
```

---

### Task 5: Adapt Deploy configuration and prepare names atomically

**Files:**
- Modify: `src/Foundry.Deploy/Models/Configuration/DeployMachineNamingSettings.cs`
- Create: `src/Foundry.Deploy/Models/Configuration/DeployMachineNameComponentSettings.cs`
- Create: `src/Foundry.Deploy/Services/Configuration/DeployMachineNamingLegacyAdapter.cs`
- Modify: `src/Foundry.Deploy/Services/Configuration/DeployConfigurationService.cs`
- Create: `src/Foundry.Deploy/Services/System/MachineNamePreparationResult.cs`
- Create: `src/Foundry.Deploy/Services/System/MachineNamePreparationService.cs`
- Modify: `src/Foundry.Deploy/Services/System/ComputerNameSuffixGenerator.cs` and rename to `MachineNameRandomGenerator.cs`
- Modify: `src/Foundry.Deploy/Services/Startup/DeploymentStartupSnapshot.cs`
- Modify: `src/Foundry.Deploy/Services/Startup/DeploymentStartupCoordinator.cs`
- Modify: `src/Foundry.Deploy/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `src/Foundry.Deploy.Tests/DeployConfigurationServiceTests.cs`
- Test: `src/Foundry.Deploy.Tests/MachineNamePreparationServiceTests.cs`

**Interfaces:**
- Consumes: schema-12 settings, Core composer, and Task 4 hardware profile.
- Produces: `MachineNamePreparationService.Prepare(settings, hardware, existingName)` and a snapshot `MachineNamePreparation` result.

- [ ] **Step 1: Write failing legacy-adapter tests**

Cover schema-11 generated, manual, and disabled JSON plus schema-12 structured preservation. Assert newly serialized configuration has no legacy properties.

- [ ] **Step 2: Implement the Deploy adapter**

Normalize legacy data immediately after deserialization:

```csharp
DeployMachineNamingSettings normalized = sourceSchemaVersion < 12
    ? DeployMachineNamingLegacyAdapter.Adapt(settings)
    : settings;
```

Keep compatibility properties nullable and ignored when writing null, or isolate them in an internal DTO. Do not let legacy fields override schema-12 fields.

- [ ] **Step 3: Write failing preparation tests**

Cover disabled/offline behavior, manual initial name, composed hardware values, KeepRight serial, locked/editable results, missing and placeholder failures, and one stable random value per preparation.

```csharp
MachineNamePreparationResult result = service.Prepare(settings, hardware, "OLD-NAME");
Assert.Equal("PC-789012345678", result.ComputerName);
Assert.False(result.IsReadOnly);
Assert.Null(result.Failure);
```

- [ ] **Step 4: Implement preparation and startup flow**

Inject `Func<int, string>` into an internal constructor for deterministic random tests. Generate only the configured random length, map Deploy records to shared Core definitions, and call the composer once after configuration, offline name, and hardware tasks have all completed. Put the result into `DeploymentStartupSnapshot` so the UI never sees a transient unresolved composition.

- [ ] **Step 5: Run focused Deploy tests**

```powershell
dotnet build src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 6: Commit runtime preparation**

```powershell
git add src/Foundry.Deploy src/Foundry.Deploy.Tests
git commit -m "feat(deploy): prepare composed machine names from hardware"
```

---

### Task 6: Simplify Deploy target editing and launch validation

**Files:**
- Modify: `src/Foundry.Deploy/ViewModels/DeploymentPreparationViewModel.cs`
- Modify: `src/Foundry.Deploy/Services/Wizard/DeploymentWizardContext.cs`
- Modify: `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Foundry.Deploy/Services/Deployment/DeploymentLaunchRequest.cs`
- Modify: `src/Foundry.Deploy/Services/Deployment/DeploymentLaunchPreparationService.cs`
- Modify: `src/Foundry.Deploy/Views/Wizard/TargetStepView.xaml`
- Modify: `src/Foundry.Deploy/Views/Wizard/TargetStepView.xaml.cs`
- Test: `src/Foundry.Deploy.Tests/DeploymentPreparationViewModelTests.cs`
- Test: `src/Foundry.Deploy.Tests/DeploymentLaunchPreparationServiceTests.cs`

**Interfaces:**
- Consumes: startup `MachineNamePreparationResult` from Task 5.
- Produces: full-name edit/read-only state and final readiness without prefix/suffix semantics.

- [ ] **Step 1: Replace obsolete tests with failing full-name tests**

Test applying locked, editable, and failed preparation results; editing the complete name; invalid final names; and launch rejection when naming preparation failed.

```csharp
viewModel.ApplyMachineNamePreparation(new("PC-123", isReadOnly: true, failure: null));
Assert.Equal("PC-123", viewModel.TargetComputerName);
Assert.True(viewModel.IsTargetComputerNameReadOnly);
Assert.True(viewModel.IsTargetComputerNameValid);
```

- [ ] **Step 2: Simplify the view model and launch request**

Remove prefix/suffix state and helpers. Bind the target field directly to `TargetComputerName`, retain Core validation, and carry a localized composition failure until a valid editable override is permitted. Remove `RequiredComputerNamePrefix` from the request and service.

- [ ] **Step 3: Update the WPF target view**

Remove the separate prefix `TextBlock`. Keep `MaxLength=15`, read-only binding, paste filtering, and input filtering, but import `Foundry.Core.Services.Configuration.ComputerNameRules` instead of the duplicate Deploy rules.

- [ ] **Step 4: Run focused tests and compile WPF**

```powershell
dotnet build src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: PASS.

- [ ] **Step 5: Commit Deploy editing changes**

```powershell
git add src/Foundry.Deploy src/Foundry.Deploy.Tests
git commit -m "refactor(deploy): edit complete composed machine names"
```

---

### Task 7: Build the consistent WinUI 3 composition editor

**Files:**
- Create: `src/Foundry/ViewModels/CustomizationConfigurationViewModel.MachineNaming.cs`
- Create: `src/Foundry/ViewModels/MachineNameComponentRowViewModel.cs`
- Modify: `src/Foundry/ViewModels/CustomizationConfigurationViewModel.cs`
- Modify: `src/Foundry/Services/Configuration/FoundryConfigurationStateService.cs`
- Modify: `src/Foundry/ViewModels/StartMediaViewModel.cs`
- Modify: `src/Foundry/Views/MachineNamingPage.xaml`

**Interfaces:**
- Consumes: Core settings, validator, and composer.
- Produces: ordered component editing, representative preview, budget, and state persistence.

- [ ] **Step 1: Extract machine-naming state into a focused partial class**

Expose collections and state equivalent to:

```csharp
public ObservableCollection<MachineNameComponentRowViewModel> MachineNameComponents { get; } = [];
public MachineNamingMode SelectedMachineNamingMode { get; set; }
public string ManualInitialValue { get; set; } = string.Empty;
public MachineNameSeparator SelectedMachineNameSeparator { get; set; }
public MachineNameCasing SelectedMachineNameCasing { get; set; }
public bool AllowMachineNameEditingDuringDeployment { get; set; } = true;
public string MachineNamePreviewText { get; }
public string MachineNameBudgetText { get; }
```

The parent owns add/remove/order changes and subscribes to row changes. The row owns immutable component type, applicable values, localized display text, and callback commands for move up, move down, and remove. Refresh available add choices after every collection change.

- [ ] **Step 2: Persist invalid drafts while blocking readiness**

Do not repeat the current early-return behavior that leaves an older valid state active. Save the structured draft, let Core overview validation return NeedsAttention, and make media generation reject the invalid configuration.

- [ ] **Step 3: Replace the Machine Naming page XAML**

Keep the existing shell. Use SettingsCards for mode, manual initial value, separator, casing, add control, preview/budget, and deployment editing. Render component rows as SettingsExpanders with applicable nested SettingsCards.

Use only repository resources:

```xml
<StackPanel Spacing="{ThemeResource FoundrySettingsSectionSpacing}">
    <wct:SettingsCard Header="{x:Bind ViewModel.MachineNamingModeLabel, Mode=OneWay}">
        <ComboBox ItemsSource="{x:Bind ViewModel.MachineNamingModeOptions, Mode=OneWay}" />
    </wct:SettingsCard>
</StackPanel>
```

Use `NumberBox` with 1-15 bounds and compact spin buttons. Use normal focusable Buttons with localized automation names and tooltips for move/remove. Disable up on the first row and down on the last. Do not require drag-and-drop.

- [ ] **Step 4: Compile and manually inspect the UI**

```powershell
dotnet build src/Foundry/Foundry.csproj -c Release -p:Platform=x64
```

Inspect normal and narrow widths, 200% text scaling, keyboard order, focus, light/dark themes, long French/German strings, and RTL Arabic/Hebrew. Compare spacing and typography to General Configuration, OOBE, Ethernet 802.1X, Wi-Fi, and Autopilot pages.

- [ ] **Step 5: Commit the authoring UI**

```powershell
git add src/Foundry
git commit -m "feat(ui): add machine name composition editor"
```

---

### Task 8: Localize authoring and deployment behavior

**Files:**
- Modify: `src/Foundry/Strings/*/Resources.resw`
- Modify: `src/Foundry.Deploy/Strings/*/Resources.resx`
- Test: `src/Foundry.Localization.Tests/ResourceKeyParityTests.cs`
- Test: `src/Foundry.Deploy.Tests/LocalizationResourceTests.cs` only if explicit runtime-key coverage adds value beyond parity.

**Interfaces:**
- Produces: complete translations for every new or changed resource key in all shipped locales.

- [ ] **Step 1: Add English resource keys and wire them before bulk translation**

Add mode, component, add/remove/move, separator, casing, truncation, static text, maximum length, preview, budget, deployment editing, validation, and missing hardware messages. Use indexed placeholders consistently, for example:

```xml
<data name="Customization.MachineNamingCharacterBudgetFormat" xml:space="preserve">
  <value>{0} characters configured, {1} remaining</value>
</data>
```

- [ ] **Step 2: Run parity tests and confirm RED**

```powershell
dotnet build src/Foundry.Localization.Tests/Foundry.Localization.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Localization.Tests/Foundry.Localization.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Expected: failure listing missing keys in non-English resources.

- [ ] **Step 3: Translate every resource**

Update all existing Foundry `.resw` and Deploy `.resx` locales. Preserve placeholders exactly, use established terminology from nearby machine-name and hardware strings, and remove obsolete prefix/generated-suffix resources after references are gone.

- [ ] **Step 4: Run localization and application builds**

```powershell
dotnet build src/Foundry.Localization.Tests/Foundry.Localization.Tests.csproj -c Release -p:Platform=x64
dotnet run --project src/Foundry.Localization.Tests/Foundry.Localization.Tests.csproj -c Release -p:Platform=x64 --no-build
dotnet build src/Foundry.slnx -c Release -p:Platform=x64 --no-restore --nologo
```

Expected: PASS with no missing keys or placeholder mismatches; build has no new warnings.

- [ ] **Step 5: Commit localization**

```powershell
git add src/Foundry/Strings src/Foundry.Deploy/Strings src/Foundry.Localization.Tests src/Foundry.Deploy.Tests
git commit -m "feat(localization): translate machine name composition"
```

---

### Task 9: Remove obsolete naming code and verify the product branch

**Files:**
- Delete when unused: `src/Foundry.Core/Services/Configuration/MachineNamingRules.cs`
- Delete: `src/Foundry.Deploy/Validation/ComputerNameRules.cs`
- Delete or rename: `src/Foundry.Deploy/Services/System/ComputerNameSuffixGenerator.cs`
- Delete or replace: `src/Foundry.Deploy.Tests/ComputerNameRulesTests.cs`
- Modify: any remaining callers or obsolete tests found by the searches below.

**Interfaces:**
- Produces: one shared naming implementation with narrowly isolated legacy adapters only.

- [ ] **Step 1: Search for obsolete symbols**

```powershell
rg -n "MachineNamingRules|ComputerNameSuffixGenerator|RequiredComputerNamePrefix|TargetComputerNamePrefix|TargetComputerNameInput|AutoGenerateName|AllowManualSuffixEdit|MachineNamingPrefix|Foundry\.Deploy\.Validation\.ComputerNameRules" src
```

- [ ] **Step 2: Remove obsolete production and test code**

Delete only code made redundant by this feature. Keep nullable JSON aliases or adapters required to read schema 13/11, but ensure they cannot be serialized into schema 14/12.

- [ ] **Step 3: Run formatting and the full x64 verification gate**

```powershell
.\scripts\Test-FoundryFormat.ps1
```

If it fails:

```powershell
.\scripts\Format-Foundry.ps1
git diff --check
.\scripts\Test-FoundryFormat.ps1
```

Then run:

```powershell
dotnet restore .\src\Foundry.slnx --nologo
dotnet build .\src\Foundry.slnx -c Release -p:Platform=x64 -p:ContinuousIntegrationBuild=true --no-restore --nologo
$testProjects = Get-ChildItem .\src -Directory -Filter *.Tests |
    ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" }
foreach ($testProject in $testProjects) {
    dotnet run --project $testProject -c Release -p:Platform=x64 --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: format passes, build exits 0 with no new warnings, and every test project reports zero failures.

- [ ] **Step 4: Request read-only code review and fix findings**

Provide the reviewer the spec, plan, base SHA, head SHA, and diff. Fix every Critical and Important issue, rerun affected tests, then rerun the full verification gate.

- [ ] **Step 5: Commit cleanup**

```powershell
git add -A
git commit -m "refactor(naming): remove obsolete prefix and suffix code"
```

---

### Task 10: Update GitBook in a separate worktree and open both PRs

**Files:**
- Separate repository: `E:\Github\Foundry Project\GitBook`
- Separate worktree: `E:\Github\Foundry Project\worktrees\machine-name-composition-docs`
- Modify: `foundry-osd/customization/machine-naming.md`
- Modify: `foundry-deploy/target.md`

**Interfaces:**
- Consumes: verified final behavior and UI from Tasks 1-9.
- Produces: product PR, documentation PR, and GitBook preview URL.

- [ ] **Step 1: Synchronize GitBook main and create the documentation worktree**

```powershell
git -C "E:\Github\Foundry Project\GitBook" fetch origin main
git -C "E:\Github\Foundry Project\GitBook" pull --ff-only origin main
git -C "E:\Github\Foundry Project\GitBook" worktree add "E:\Github\Foundry Project\worktrees\machine-name-composition-docs" -b "docs/machine-name-composition"
```

- [ ] **Step 2: Update the two canonical pages**

Document modes, every component source, one-instance rule, ordering, separator/casing, per-component length and keep side, rightmost serial default, sanitization, placeholder blocking, representative preview, deployment editing, migration behavior, and examples. Update screenshots only if the final UI materially differs and sanitize all identifiers.

- [ ] **Step 3: Validate and commit GitBook**

Validate relative Markdown targets, confirm both canonical pages remain in `SUMMARY.md`, inspect `git diff --check`, and commit:

```powershell
$missingTargets = @()
Get-ChildItem -Recurse -File -Filter *.md | ForEach-Object {
    $source = $_
    $content = Get-Content -Raw -LiteralPath $source.FullName
    [regex]::Matches($content, '!?' + '\[[^\]]*\]\((?!https?://|mailto:|#)([^)#]+)(?:#[^)]*)?\)') | ForEach-Object {
        $relativeTarget = [uri]::UnescapeDataString($_.Groups[1].Value)
        $targetPath = Join-Path $source.DirectoryName $relativeTarget
        if (-not (Test-Path -LiteralPath $targetPath)) {
            $missingTargets += "$($source.FullName): $relativeTarget"
        }
    }
}
if ($missingTargets.Count -gt 0) { $missingTargets; exit 1 }
Select-String -Path SUMMARY.md -SimpleMatch 'foundry-osd/customization/machine-naming.md'
Select-String -Path SUMMARY.md -SimpleMatch 'foundry-deploy/target.md'
git diff --check
git add foundry-osd/customization/machine-naming.md foundry-deploy/target.md
git commit -m "docs(naming): document machine name composition"
```

- [ ] **Step 4: Push and open the product PR**

```powershell
git push -u origin feat/machine-name-composition
$productPrBody = @'
## Summary
- add structured machine-name composition
- resolve supported hardware identifiers during deployment
- update localized authoring and deployment experiences

## Reason
Give administrators flexible machine naming without exposing a template language.

## Main changes
- add ordered, validated naming components and legacy configuration migration
- add asset tag and SMBIOS UUID discovery
- update privacy-safe telemetry and remove obsolete prefix/suffix code

## Testing
- `scripts/Test-FoundryFormat.ps1`
- x64 Release solution build
- all x64 test projects
- manual WinUI review at narrow width, 200% text scaling, light/dark, long-text, and RTL layouts

## Schema changes
- Foundry: 13 to 14
- Deploy: 11 to 12

CI x64 and ARM64 checks are pending.
'@
gh pr create --repo foundry-osd/foundry --base main --head feat/machine-name-composition --title "feat(naming): add machine name composition" --body $productPrBody
```

The body includes summary, reason, main changes, schema changes, cleanup, translations, formatting, build, test counts, manual UI review, and pending x64/ARM64 CI notes. Do not squash or merge.

- [ ] **Step 5: Push and open the GitBook PR**

```powershell
git push -u origin docs/machine-name-composition
$docsPrBody = @'
## Summary
Document Foundry machine-name composition and deployment behavior.

## Reason
Administrators need complete guidance for hardware-backed naming, validation, and technician editing.

## Main changes
- document composition controls and supported hardware sources
- document truncation, sanitization, casing, separators, and validation
- document Deploy resolution and failure behavior

## Testing
- validated GitBook structure and relative links
- ran `git diff --check`
'@
gh pr create --repo foundry-osd/GitBook --base main --head docs/machine-name-composition --title "docs(naming): document machine name composition" --body $docsPrBody
```

The body includes summary, reason, main changes, and validation notes. Do not squash or merge.

- [ ] **Step 6: Retrieve the GitBook preview**

```powershell
gh pr view --repo foundry-osd/GitBook --json number,url,statusCheckRollup
```

Return the `https://docs.foundryosd.com/~/revisions/.../` target URL from the `GitBook - docs.foundryosd.com/` check. If the check has not published a URL yet, report it as pending rather than waiting for CI unless the user asks to wait.

- [ ] **Step 7: Report delivery state**

Return both PR URLs, the GitBook preview URL or pending state, exact verification evidence, schema changes, and the preserved worktree paths. Leave both branches and worktrees intact for review.

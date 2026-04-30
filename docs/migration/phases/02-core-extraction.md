# Core Extraction Phases

## Phase 4: Create Foundry.Core

**Priority:** critical.

**Goal:** create the UI-neutral project that receives business logic before UI porting begins.

- [ ] **4.1** Create `src\Foundry.Core\Foundry.Core.csproj`.
- [ ] **4.2** Target `net10.0-windows`.
- [ ] **4.3** Treat `Foundry.Core` as UI-neutral but Windows-specific business logic.
- [ ] **4.4** Add only necessary dependencies:
  - [ ] `Microsoft.Extensions.Logging.Abstractions` if logging abstractions are required.
  - [ ] `System.Text.Json` through the shared framework where possible.
  - [ ] Avoid WPF/WinUI references.
- [ ] **4.5** Create `src\Foundry.Core.Tests\Foundry.Core.Tests.csproj` from scratch.
- [ ] **4.6** Use a clean test folder structure:
  - [ ] `Configuration`.
  - [ ] `Provisioning`.
  - [ ] `Localization`.
  - [ ] `WinPe`.
  - [ ] `Drivers`.
  - [ ] `Updates` only if update decision logic is UI-neutral.
- [ ] **4.7** Move business models first:
  - [ ] `Models\Configuration`.
  - [ ] `Models\Configuration\Deploy`.
- [ ] **4.8** Move pure configuration services:
  - [ ] `ConfigurationJsonDefaults`.
  - [ ] `ExpertConfigurationService`.
  - [ ] `DeployConfigurationGenerator`.
  - [ ] `EmbeddedLanguageRegistryService`.
  - [ ] `LanguageCodeUtility`.
- [ ] **4.9** Move pure WinPE value objects and helpers:
  - [ ] `WinPeArchitecture`.
  - [ ] `WinPeArchitectureExtensions`.
  - [ ] `WinPeSignatureMode`.
  - [ ] `UsbPartitionStyle`.
  - [ ] `UsbFormatMode`.
  - [ ] `WinPeErrorCodes`.
  - [ ] `WinPeDiagnostic`.
  - [ ] `WinPeResult`.
  - [ ] `WinPeHashHelper`.
  - [ ] `WinPeFileSystemHelper` if it has no UI dependency.
- [ ] **4.10** Keep shell/UI services out of `Foundry.Core`:
  - [ ] `ApplicationShellService`.
  - [ ] Theme services.
  - [ ] WinUI dialogs.
  - [ ] WPF dialogs.
- [ ] **4.11** Allow Windows business services in `Foundry.Core` when they do not depend on WPF or WinUI:
  - [ ] ADK detection/orchestration.
  - [ ] WinPE build orchestration.
  - [ ] Driver catalog/resolution/injection.
  - [ ] ISO/USB media business operations.
- [ ] **4.12** Move embedded assets needed by core:
  - [ ] `Assets\Configuration\languages.json`.
  - [ ] `Assets\Configuration\iana-windows-timezones.json`.
- [ ] **4.13** Keep executable/runtime assets in the app unless core owns the resolution contract:
  - [ ] `Assets\7z`.
  - [ ] `Assets\WinPe\FoundryBootstrap.ps1`.
- [ ] **4.14** Rewrite only the old `Foundry.Tests` cases that protect migrated business rules.
- [ ] **4.15** Do not copy old tests mechanically.
- [ ] **4.16** Do not write UI tests.
- [ ] **4.17** Commit:

```powershell
git commit -m "refactor: extract foundry core project"
```

**Validation**

- [ ] **4.18** `dotnet test .\src\Foundry.Core.Tests\Foundry.Core.Tests.csproj -c Release --nologo`.
- [ ] **4.19** Confirm rewritten tests cover:
  - [ ] Expert configuration serialization.
  - [ ] Deploy configuration generation.
  - [ ] Language registry fallback behavior.
  - [ ] Culture/catalog behavior.

## Phase 5: Extract Migration Seams From Current WPF Reference

**Priority:** high.

**Goal:** reduce `MainWindowViewModel` risk before translating it into WinUI pages.

- [ ] **5.1** Read archived `MainWindowViewModel.cs` as reference only.
- [ ] **5.2** Identify all responsibilities:
  - [ ] App menu commands.
  - [ ] Theme selection.
  - [ ] Language selection.
  - [ ] ADK status and install/upgrade.
  - [ ] ISO output selection.
  - [ ] USB candidate refresh.
  - [ ] ISO creation.
  - [ ] USB creation.
  - [ ] Expert configuration import/export.
  - [ ] Deploy configuration export.
  - [ ] Progress/status display.
  - [ ] Update check.
  - [ ] About/help links.
- [ ] **5.3** Create thin service contracts for UI-facing operations:
  - [ ] `IFilePickerService`.
  - [ ] `IDialogService`.
  - [ ] `IApplicationLifetimeService`.
  - [ ] `IExternalProcessLauncher`.
  - [ ] `IAppDispatcher`.
- [ ] **5.4** Keep service interfaces platform-neutral where possible.
- [ ] **5.5** Implement WinUI versions in the `Foundry` app project, not in `Foundry.Core`.
- [ ] **5.6** Identify duplicated or awkward logic that can be simplified during extraction without changing runtime behavior.
- [ ] **5.7** Record any targeted `Foundry.Connect` or `Foundry.Deploy` changes needed to simplify shared contracts.
- [ ] **5.8** Keep WPF reference unchanged.
- [ ] **5.9** Commit:

```powershell
git commit -m "refactor: define winui application service boundaries"
```

**Validation**

- [ ] **5.10** Confirm `Foundry.Core` has no dependency on `Microsoft.UI.Xaml`.
- [ ] **5.11** Confirm `Foundry.Core` has no dependency on `System.Windows`.
- [ ] **5.12** Confirm app project owns all WinUI-specific service implementations.
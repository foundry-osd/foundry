# Core Extraction Phases

## Phase 4: Create Foundry.Core

**Priority:** critical.

**Goal:** create the UI-neutral project that receives business logic before UI porting begins.

- [x] **4.1** Create `src\Foundry.Core\Foundry.Core.csproj`.
- [x] **4.2** Target `net10.0-windows`.
- [x] **4.3** Treat `Foundry.Core` as UI-neutral but Windows-specific business logic.
- [x] **4.4** Add only necessary dependencies:
  - [x] `Microsoft.Extensions.Logging.Abstractions` if logging abstractions are required. Not required in this phase.
  - [x] `System.Text.Json` through the shared framework where possible.
  - [x] Avoid WPF/WinUI references.
- [x] **4.5** Create `src\Foundry.Core.Tests\Foundry.Core.Tests.csproj` from scratch.
- [x] **4.6** Use a clean test folder structure:
  - [x] `Configuration`.
  - [ ] `Provisioning`. Deferred until provisioning logic is extracted.
  - [x] `Localization`.
  - [x] `WinPe`.
  - [ ] `Drivers`. Deferred until driver logic is extracted.
  - [x] `Updates` only if update decision logic is UI-neutral. Not useful in this phase.
- [x] **4.7** Move business models first:
  - [x] `Models\Configuration`.
  - [x] `Models\Configuration\Deploy`.
- [x] **4.8** Move pure configuration services:
  - [x] `ConfigurationJsonDefaults`.
  - [x] `ExpertConfigurationService`.
  - [x] `DeployConfigurationGenerator`.
  - [x] `EmbeddedLanguageRegistryService`.
  - [x] `LanguageCodeUtility`.
- [x] **4.9** Move pure WinPE value objects and helpers:
  - [x] `WinPeArchitecture`.
  - [x] `WinPeArchitectureExtensions`.
  - [x] `WinPeSignatureMode`.
  - [x] `UsbPartitionStyle`.
  - [x] `UsbFormatMode`.
  - [x] `WinPeErrorCodes`.
  - [x] `WinPeDiagnostic`.
  - [x] `WinPeResult`.
  - [x] `WinPeHashHelper`.
  - [x] `WinPeFileSystemHelper` if it has no UI dependency.
- [x] **4.10** Keep shell/UI services out of `Foundry.Core`:
  - [x] `ApplicationShellService`.
  - [x] Theme services.
  - [x] WinUI dialogs.
  - [x] WPF dialogs.
- [x] **4.11** Allow Windows business services in `Foundry.Core` when they do not depend on WPF or WinUI:
  - [ ] ADK detection/orchestration. Deferred to the ADK extraction phase.
  - [ ] WinPE build orchestration. Deferred to the WinPE build phase.
  - [ ] Driver catalog/resolution/injection. Deferred to the driver phase.
  - [ ] ISO/USB media business operations. Deferred to the media creation phase.
- [x] **4.12** Move embedded assets needed by core:
  - [x] `Assets\Configuration\languages.json`.
  - [x] `Assets\Configuration\iana-windows-timezones.json`.
- [x] **4.13** Keep executable/runtime assets in the app unless core owns the resolution contract:
  - [x] `Assets\7z`.
  - [x] `Assets\WinPe\FoundryBootstrap.ps1`.
- [x] **4.14** Rewrite only the old `Foundry.Tests` cases that protect migrated business rules.
- [x] **4.15** Do not copy old tests mechanically.
- [x] **4.16** Do not write UI tests.
- [x] **4.17** Commit:

```powershell
git commit -m "refactor: extract foundry core project"
```

**Validation**

- [x] **4.18** `dotnet test .\src\Foundry.Core.Tests\Foundry.Core.Tests.csproj -c Release --nologo`.
- [x] **4.19** Confirm rewritten tests cover:
  - [x] Expert configuration serialization.
  - [x] Deploy configuration generation.
  - [x] Language registry fallback behavior.
  - [x] Culture/catalog behavior.

## Phase 5: Extract Migration Seams From Current WPF Reference

**Priority:** high.

**Goal:** reduce `MainWindowViewModel` risk before translating it into WinUI pages.

- [x] **5.1** Read archived `MainWindowViewModel.cs` as reference only.
- [x] **5.2** Identify all responsibilities:
  - [x] Shell actions and workflow entry points from the WPF app, without migrating the old WPF menu bar 1:1.
  - [x] Theme selection.
  - [x] Language selection.
  - [x] ADK status and install/upgrade.
  - [x] ISO output selection.
  - [x] USB candidate refresh.
  - [x] ISO creation.
  - [x] USB creation.
  - [x] Legacy expert configuration document model boundaries.
  - [x] Deploy runtime configuration generation.
  - [x] Progress/status display.
  - [x] Update check.
  - [x] About/help links.
- [x] **5.3** Create thin service contracts for UI-facing operations:
  - [x] `IFilePickerService`.
  - [x] `IDialogService`.
  - [x] `IApplicationLifetimeService`.
  - [x] `IExternalProcessLauncher`.
  - [x] `IAppDispatcher`.
- [x] **5.4** Keep service interfaces platform-neutral where possible.
- [x] **5.5** Implement WinUI versions in the `Foundry` app project, not in `Foundry.Core`.
- [x] **5.6** Identify duplicated or awkward logic that can be simplified during extraction without changing runtime behavior.
- [x] **5.7** Record any targeted `Foundry.Connect` or `Foundry.Deploy` changes needed to simplify shared contracts.
- [x] **5.8** Keep WPF reference unchanged.
- [x] **5.9** Commit:

```powershell
git commit -m "refactor: define winui application service boundaries"
```

**Validation**

- [x] **5.10** Confirm `Foundry.Core` has no dependency on `Microsoft.UI.Xaml`.
- [x] **5.11** Confirm `Foundry.Core` has no dependency on `System.Windows`.
- [x] **5.12** Confirm app project owns all WinUI-specific service implementations.

**Phase 5 notes**

- [x] `Foundry.Core` owns neutral contracts only:
  - [x] Picker request/choice DTOs.
  - [x] Dialog request DTOs.
  - [x] `IFilePickerService`, `IDialogService`, `IApplicationLifetimeService`, `IExternalProcessLauncher`, `IAppDispatcher`.
- [x] `Foundry` owns WinUI implementations:
  - [x] `WinUiFilePickerService` using Windows App SDK pickers tied to the main `AppWindow.Id`.
  - [x] `WinUiDialogService` using WinUI `ContentDialog`.
  - [x] `WinUiAppDispatcher` using WinUI `DispatcherQueue`.
  - [x] `WinUiApplicationLifetimeService` using WinUI application lifetime.
  - [x] `WinUiExternalProcessLauncher` using shell execution for URLs, files, and folders.
- [x] Future simplification candidates:
  - [x] Replace broad WPF `ApplicationShellService` usage with focused picker, dialog, launcher, lifetime, and dispatcher contracts.
  - [x] Keep file/folder picking asynchronous in WinUI instead of preserving synchronous WPF dialog calls.
  - [x] Keep `MainWindowViewModel` port split by navigation page rather than recreating one large shell view model.
- [x] No targeted `Foundry.Connect` or `Foundry.Deploy` changes are needed for these service-boundary contracts.

# Findings & Decisions

## Requirements
- Create a second project `Foundry.Deploy` dedicated to WinPE OS deployment orchestration.
- Implement wizard-style UI and a deployment progress page with stages, status, logs, and errors.
- Analyze OSDCloud deeply for architecture, cache, orchestration, implementation patterns, and functional design.
- Consume existing catalogs from `Foundry.Automation`:
  - `OperatingSystem.xml`
  - `DriverPack_Unified.xml`
- Use .NET 10 WPF, self-contained, single-file publish.
- Match existing `Foundry` architecture and reuse Fluent theme.
- Support two modes:
  - USB mode with dedicated cache partition
  - ISO mode without dedicated cache partition
- Bootstrap via `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`:
  - Download `Foundry.Deploy` zip from GitHub Releases using BITS
  - Extract and execute in WinPE

## Research Findings
- Structure actuelle: solution `src/Foundry.slnx` avec un seul projet WPF `src/Foundry/Foundry.csproj`.
- Stack UI/DI:
  - WPF + `CommunityToolkit.Mvvm`
  - DI via `Microsoft.Extensions.DependencyInjection` dans `src/Foundry/Program.cs`
  - Ressources Fluent via `PresentationFramework.Fluent` dans `src/Foundry/App.xaml`
- Découpage de services réutilisable:
  - `Services/WinPe` (build image, drivers, médias USB/ISO, process runner)
  - `Services/Operations/OperationProgressService` (progress/status global, verrou d’opération unique)
  - `Services/Theme`, `Services/Localization`, `Services/ApplicationShell`
- Orchestration actuelle:
  - `MediaOutputService` en pipeline: validate -> resolve tools -> build -> drivers -> customize -> signature policy -> create media -> metadata.
  - Reporting de progression par étapes textuelles et pourcentage.
- Gestion USB actuelle:
  - Provisionnement du disque avec 2 partitions via `diskpart`:
    - `BOOT` FAT32 (~4GB)
    - `Foundry Cache` NTFS (reste du disque)
  - Création d’un répertoire `Foundry Cache` à la racine de la partition cache.
  - Vérification des artefacts boot (`boot.wim`, `BCD`, EFI).
- Gestion ISO actuelle:
  - Build workspace temporaire puis création ISO via `MakeWinPEMedia`.
  - Métadonnées JSON écrites à côté de l’ISO.
- Bootstrap WinPE:
  - Script embarqué comme ressource (`Foundry.WinPe.BootstrapScript`).
  - Fichier actuel `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1` est minimal (log de démarrage), donc la logique de téléchargement `Foundry.Deploy` reste à implémenter.
- Defaults/catalogues:
  - `WinPeDefaults.DefaultUnifiedCatalogUri` pointe actuellement vers `Foundry.Automation/Cache/WinPE/WinPE_Unified.xml` (catalogue drivers WinPE pour création media, différent du futur catalogue OS de déploiement).
- OSDCloud structure (high level):
  - PowerShell module root: `OSDCloud.psm1` + `OSDCloud.psd1`.
  - Command API split:
    - `public/` (entry cmdlets like `Deploy-OSDCloud`)
    - `private/` (internal orchestration and workflow logic)
    - `public-winpe/` and `private/pe-startup/` for WinPE startup helpers.
  - Workflow model:
    - JSON workflow/task descriptors under `workflow/*`.
    - Step scripts split by phases under `private/steps/1..9-*`.
  - Catalog model:
    - OS catalogs under `catalogs/operatingsystem`
    - Driver pack catalogs under `catalogs/driverpack/<vendor>`
  - Optional WPF UX experiments under `workflow/*/ux` (legacy .NET Framework style), while core deployment remains PowerShell-first.
- OSDCloud orchestration model (deep):
  - Entry cmdlet `public/Deploy-OSDCloud.ps1` initializes deployment context then executes either CLI or UX-triggered workflow.
  - Runtime context lives in global objects:
    - `$global:OSDCloudDevice` (hardware/device snapshot)
    - `$global:OSDCloudDeploy` (selected OS, driverpack, workflow)
    - `$global:OSDCloudWorkflowInvoke` (execution runtime, paths, timing, selected files).
  - Workflow tasks are JSON (`workflow/<name>/tasks/*.json`) and composed of ordered steps with metadata:
    - `skip`, `pause`, `testinfullos`, `parameters`, `args`, `scriptblock`.
  - Step engine `private/Invoke-OSDCloudWorkflowTask.ps1` evaluates each step and executes commands dynamically with guardrails.
- OSDCloud cache/storage conventions:
  - Main transient workspace on target disk: `C:\OSDCloud\...`
    - OS payloads: `C:\OSDCloud\OS`
    - scratch: `C:\OSDCloud\Temp`
  - Driverpack temp staging:
    - `C:\Windows\Temp\osdcloud-driverpack-download`
    - `C:\Windows\Temp\osdcloud-driverpack-expand`
  - Logs:
    - WinPE phase starts in `$env:TEMP\osdcloud-logs` (X: in WinPE)
    - later consolidated to `C:\Windows\Temp\osdcloud-logs`
    - transcript + DISM logs + final `OSDCloud.json`
  - Offline reuse strategy:
    - Before download, test online reachability (HTTP HEAD).
    - If unavailable online, search **all file system drives** for:
      - `*\OSDCloud\OS\<filename>`
      - `*\OSDCloud\DriverPacks\<vendor>\<filename>`
    - If USB drive labeled `OSDCloud|USB-DATA` exists, download there first, then copy to local temp/cache.
- OSDCloud resiliency patterns:
  - Strong preflight checks for target disk/image/driverpack.
  - Long sleep-and-stop on hard blockers (operator intervention expected).
  - Explicit SHA1/SHA256 validation for OS image when hash present.
  - Downloads via `curl.exe` with HEAD pre-check, resume (`--continue-at -`), retry/backoff.
  - Temporary removal/restoration of USB drive letters during disk operations to avoid letter conflicts.
- OSDCloud design choice relevant to Foundry.Deploy:
  - Deployment uses a declarative step list + executable actions (task-sequence style), not a monolithic script.
  - UI is a trigger/selector layer; orchestration remains independent from UI.
  - Catalog-driven selection (OS + drivers) with hardware-based defaults and user override.
- Foundry.Automation catalog schemas (validated live on 2026-02-22):
  - OS catalog URL: `.../Cache/OS/OperatingSystem.xml`
    - Root: `OperatingSystemCatalog`
    - `generatedAtUtc`: `2026-02-22T03:46:30Z`
    - Items count: `3037`
    - Structure:
      - `Sources/Source` metadata by build (`build`, `buildMajor`, `buildUbr`, `cabUrl`, checksums, date)
      - `Items/Item` entries containing:
        - `clientType`, `windowsRelease`, `releaseId`, `build*`, `architecture`
        - `languageCode`, `language`, `edition`
        - `fileName`, `sizeBytes`, `licenseChannel`, `url`
  - Driver catalog URL: `.../Cache/DriverPack/DriverPack_Unified.xml`
    - Root: `DriverPackCatalog`
    - `generatedAtUtc`: `2026-02-22T03:48:35Z`
    - `totalItems`: `8503`
    - Manufacturers present: `Dell`, `HP`, `Lenovo`, `Microsoft`
    - Structure:
      - `DriverPacks/DriverPack` with attributes (`id`, `packageId`, `manufacturer`, `version`, `downloadUrl`, `releaseDate`, etc.)
      - nested `Models/Model`, `OsInfo`, and `Hashes`.
- Gap found in current repo:
  - Referenced draft file `.sisyphus/drafts/foundry-deploy.md` is not present in `e:\\Github\\Foundry` at analysis time.
- Implementation snapshot (current turn):
  - Added new WPF project: `src/Foundry.Deploy/Foundry.Deploy.csproj`.
  - Added project to solution: `src/Foundry.slnx`.
  - Implemented base DI + app startup:
    - `src/Foundry.Deploy/Program.cs`
    - `src/Foundry.Deploy/App.xaml`
  - Implemented Fluent theme reuse pattern:
    - `src/Foundry.Deploy/Services/Theme/IThemeService.cs`
    - `src/Foundry.Deploy/Services/Theme/ThemeService.cs`
  - Implemented operation progress service pattern (aligned to Foundry):
    - `src/Foundry.Deploy/Services/Operations/*`
  - Implemented OS/Driver catalog consumers against Foundry.Automation XML feeds:
    - `src/Foundry.Deploy/Services/Catalog/OperatingSystemCatalogService.cs`
    - `src/Foundry.Deploy/Services/Catalog/DriverPackCatalogService.cs`
  - Upgraded orchestration from scaffold to destructive WinPE deployment actions:
    - target disk partitioning (`diskpart` GPT + EFI/Windows)
    - OS apply (`dism /Apply-Image` with image index resolution)
    - boot setup (`bcdboot` UEFI)
    - offline driver injection (`dism /Add-Driver /Recurse`)
    - files:
      - `src/Foundry.Deploy/Services/Deployment/WindowsDeploymentService.cs`
      - `src/Foundry.Deploy/Services/Deployment/IWindowsDeploymentService.cs`
      - `src/Foundry.Deploy/Services/Deployment/DeploymentTargetLayout.cs`
      - `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
  - Added explicit target disk requirement in deployment context + wizard UI:
    - `src/Foundry.Deploy/Services/Deployment/DeploymentContext.cs`
    - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
    - `src/Foundry.Deploy/MainWindow.xaml`
  - Replaced manual disk number input with assisted target disk discovery (`Get-Disk`):
    - new model + service:
      - `src/Foundry.Deploy/Models/TargetDiskInfo.cs`
      - `src/Foundry.Deploy/Services/Hardware/ITargetDiskService.cs`
      - `src/Foundry.Deploy/Services/Hardware/TargetDiskService.cs`
    - UI/VM integration:
      - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
      - `src/Foundry.Deploy/MainWindow.xaml`
    - behavior:
      - auto-load and refresh disk list
      - default selection on first safe disk
      - blocked disks (`system`, `boot`, `read-only`, `offline`) cannot start deployment
  - Added explicit destructive action confirmation dialog before deployment launch:
    - includes selected disk number/model/bus/size and selected OS details
    - cancellation stops before orchestration starts
    - files:
      - `src/Foundry.Deploy/Services/ApplicationShell/IApplicationShellService.cs`
      - `src/Foundry.Deploy/Services/ApplicationShell/ApplicationShellService.cs`
      - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
      - `src/Foundry.Deploy/Program.cs`
  - Added automatic Debug Safe Mode (Visual Studio debug execution):
    - activation: debugger attached in `DEBUG` build
    - effect:
      - deployment context marked `IsDryRun=true`
      - orchestrator simulates all steps (`download/apply-image/drivers/autopilot`) without destructive actions
      - no disk erase confirmation required in debug safe mode
      - virtual debug target disk is available when no safe disk is selectable
    - files:
      - `src/Foundry.Deploy/Services/Runtime/DebugSafetyMode.cs`
      - `src/Foundry.Deploy/Services/Deployment/DeploymentContext.cs`
      - `src/Foundry.Deploy/Services/Deployment/DeploymentRuntimeState.cs`
      - `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
      - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
      - `src/Foundry.Deploy/MainWindow.xaml`
  - Upgraded Autopilot flow from artifact-prep-only to full runtime execution:
    - ensures `Get-WindowsAutopilotInfo`
    - exports hardware hash CSV
    - runs online registration/assignment (`-Online -Assign -GroupTag Foundry`)
    - writes transcript + workflow manifest + offline marker under deployed OS
    - files:
      - `src/Foundry.Deploy/Services/Autopilot/AutopilotService.cs`
      - `src/Foundry.Deploy/Services/Autopilot/IAutopilotService.cs`
      - `src/Foundry.Deploy/Services/Autopilot/AutopilotExecutionResult.cs`
  - Implemented wizard + progress UI scaffold:
    - `src/Foundry.Deploy/MainWindow.xaml`
    - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
  - Upgraded WinPE bootstrap to release-driven launcher (latest channel, BITS download, optional digest verification, extraction, execution):
    - `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`
  - Added publish profiles for self-contained single-file builds:
    - `src/Foundry.Deploy/Properties/PublishProfiles/win-x64.pubxml`
    - `src/Foundry.Deploy/Properties/PublishProfiles/win-arm64.pubxml`

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Start with architecture-first deliverable (analysis + plan) | User asked to analyze existing and produce implementation plan before full build |
| Reuse current service layering and DI style for `Foundry.Deploy` | Minimizes cognitive load and keeps both projects consistent |
| Keep Fluent theme mechanism identical | Requested UI consistency between Foundry and Foundry.Deploy |
| Include full Autopilot in v1 | User explicitly confirmed full Autopilot scope |
| Allow ISO-mode cache in `C:\\Foundry\\Deploy` | User explicitly authorized local ISO cache |
| Use permanent `latest` bootstrap channel | User explicitly selected latest release policy |
| Expose all OS catalog editions/languages | User explicitly requested full catalog exposure |
| Enforce zero telemetry | User explicitly requested no telemetry (unlike OSDCloud) |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| `rg` regex query failed due invalid escaped sequence in first attempt | Switched to fixed-string searches (`rg -F`) and targeted patterns |
| `.sisyphus/drafts/foundry-deploy.md` missing in workspace | Proceeded with direct source analysis from `src/Foundry` and OSDCloud repo |
| `NETSDK1032` RID/platform mismatch on `Foundry.Deploy` build | Moved self-contained/single-file settings to publish profiles and kept project build RID-agnostic |
| `ThemeMode` ambiguity (`System.Windows.ThemeMode` vs custom enum) | Added explicit alias in ViewModel and used aliased enum |
| Missing `System.IO` namespace in new files | Added `using System.IO;` in impacted files |
| Remaining build errors in `DriverPackPreparationService` (`File/Path/Directory` unresolved) | Added missing `using System.IO;` and rebuilt solution |

## Resources
- Local repo: `e:\\Github\\Foundry`
- OSDCloud repo: `https://github.com/OSDeploy/OSDCloud`
- Catalog (OS): `https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/OS/OperatingSystem.xml`
- Catalog (DriverPack): `https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/DriverPack/DriverPack_Unified.xml`
- Key files:
  - `src/Foundry/Program.cs`
  - `src/Foundry/App.xaml`
  - `src/Foundry/Services/Operations/OperationProgressService.cs`
  - `src/Foundry/Services/WinPe/MediaOutputService.cs`
  - `src/Foundry/Services/WinPe/WinPeUsbMediaService.cs`
  - `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`

## Visual/Browser Findings
- OSDCloud and remote catalogs were already analyzed earlier in this session and captured above.

## Audit Notes
- 2026-02-22: Reviewed `MainWindowViewModel` and `DeploymentOrchestrator` to map the wizard UI to orchestration steps and logging flow; noted that the view model blindly confirms wizard steps and selects target disks before hitting orchestrator.
- 2026-02-22: Reviewed `WindowsDeploymentService` and `AutopilotService`; disk partitioning always does `clean`/`convert gpt` without re-checking disk state, and the autopilot script writes manifests even when registration fails.
- 2026-02-22: Checked `DeploymentContext`/`DeploymentRuntimeState`; context lacks validation for driver pack or cache path and runtime state never tracks failure markers beyond exception-driven logs, leaving resume state unclear.
- 2026-02-22: Cache resolution favors ISO root when ISO mode is set but falls back immediately to `X:\Windows\Temp\Foundry\Deploy` when USB cache partition lacks the expected label/folder; `TargetDiskService` blocks the first system/boot/read-only/offline disk but still returns them, trusting the UI to handle warnings.
- 2026-02-22: Driver pack extraction deletes and reuses the same folder, which is fine, but `DriverPackPreparationService` silently delegates unsupported archives to deferred install; `OperationProgressService` resets after 5 seconds even on failure, which could obscure history while the UI still shows running state.
- 2026-02-22: Deployment logging always writes a new log file named with timestamp and rewrites `deployment-state.json` each time, so failed steps don't accumulate multiple snapshots; no cleanup path for stale `State` files if cache root switches.
- 2026-02-22: `ArtifactDownloadService` prefers BITS but never tries to resume partial downloads if BITS fails, and `ProcessRunner` kills processes on cancellation without checking for privileged elevation (could still leave diskpart or powershell orphaned logs).
- 2026-02-22: `HardwareProfileService` gracefully degrades, but `IsAutopilotCapable` requires TPM and serial; if `Get-CimInstance` returns partial data, autopilot may still run and fail later since the UI always defaults to full Autopilot when the flag toggled on by default.
- 2026-02-22: `MainWindow` wiring exposes `CacheRootPath` text box but `DeploymentOrchestrator` still resolves cache from `DeploymentMode`; editing the textbox does nothing after the UI re-triggers `EnsureCachePathForMode`, so user edits are silently overwritten.
- 2026-02-22: `ApplicationShellService.ConfirmWarning` is modal and blocking but there's no timeout or cancellation path, so the orchestrator can remain stuck at the confirmation dialog if WinPE UI is unattended, leaving diskpart ready to run while waiting for operator action.

## Audit Remediation (2026-02-22)
- Corrected debug-safe cache behavior outside WinPE:
  - `MainWindowViewModel.EnsureCachePathForMode` now uses `%TEMP%\\Foundry\\Deploy\\Debug` in debug-safe mode instead of `X:\\...`.
  - file: `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`.
- Added orchestrator-side disk revalidation (independent of UI) before destructive actions:
  - validates selected disk still exists and remains eligible before `diskpart clean`.
  - file: `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`.
- Added cache-path-to-disk topology check to prevent ISO cache/target disk collision:
  - detects if resolved cache root is on same disk as deployment target and auto-fallbacks to transient safe cache.
  - files:
    - `src/Foundry.Deploy/Services/Hardware/ITargetDiskService.cs`
    - `src/Foundry.Deploy/Services/Hardware/TargetDiskService.cs`
    - `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`.
- Hardened log session initialization:
  - if preferred cache path is invalid/unavailable, logger falls back to transient path to avoid startup crash.
  - file: `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`.
- Implemented Autopilot deferred completion strategy:
  - added deployment option `AllowAutopilotDeferredCompletion` with UI toggle.
  - if online enrollment fails and deferred mode is enabled:
    - deployment no longer fails globally,
    - deferred script is written into deployed OS (`C:\ProgramData\Foundry\Autopilot\Invoke-FoundryAutopilot-Deferred.ps1`),
    - `SetupComplete.cmd` hook is added/extended to execute deferred script on first boot,
    - status/manifest reflect `state=deferred`.
  - if deferred mode is disabled, behavior remains strict (Autopilot step fails deployment).
  - files:
    - `src/Foundry.Deploy/Services/Autopilot/AutopilotService.cs`
    - `src/Foundry.Deploy/Services/Autopilot/IAutopilotService.cs`
    - `src/Foundry.Deploy/Services/Autopilot/AutopilotExecutionResult.cs`
    - `src/Foundry.Deploy/Services/Deployment/DeploymentContext.cs`
    - `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
    - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
    - `src/Foundry.Deploy/MainWindow.xaml`.

# Media And WinPE Phases

## Phase 12: General Media Creation Workflow

**Priority:** medium-high after Phase 13 service prerequisites.

**Goal:** port the standard ISO/USB creation workflow into the `Start` page and blocking operation overlay model.

**Prerequisites:** Phase 11 shell/overlay contract and the Phase 13 ADK/WinPE service contract must be available before implementing the final media creation commands.

- [ ] **12.1** Create WinUI view model for media creation on the `Start` page.
- [ ] **12.2** Port state from WPF `MainWindowViewModel`:
  - [ ] ISO output path.
  - [ ] Architecture.
  - [ ] CA 2023 signature mode.
  - [ ] USB partition style.
  - [ ] USB format mode.
  - [ ] Dell driver inclusion.
  - [ ] HP driver inclusion.
  - [ ] Custom driver directory.
  - [ ] Selected USB disk.
  - [ ] Selected WinPE language.
- [ ] **12.3** Port commands:
  - [ ] Browse ISO path.
  - [ ] Browse custom driver folder.
  - [ ] Refresh USB disks.
  - [ ] Create ISO.
  - [ ] Create USB.
- [ ] **12.4** Implement WinUI warning dialog for destructive USB formatting.
- [ ] **12.5** Use app dispatcher abstraction for UI updates.
- [ ] **12.6** Keep media build service logic in core/app services, not page code-behind.
  - [ ] **12.6.1** Show a final execution summary before ISO or USB creation:
    - [ ] ADK status.
    - [ ] WinPE language.
    - [ ] Architecture.
    - [ ] ISO output path.
    - [ ] USB target.
    - [ ] Runtime payload readiness.
    - [ ] Driver options.
    - [ ] Network validation.
  - [ ] **12.6.2** Run ISO creation inside the blocking operation overlay.
  - [ ] **12.6.3** Run USB creation inside the blocking operation overlay.
  - [ ] **12.6.4** Keep navigation blocked until ISO or USB creation fully completes.
- [ ] **12.7** Commit:

```powershell
git commit -m "feat: port media creation workflow to winui"
```

**Validation**

- [ ] **12.8** ADK missing state disables media creation.
- [ ] **12.9** Invalid ISO path disables ISO creation.
- [ ] **12.10** No USB candidate disables USB creation.
- [ ] **12.11** ARM64 enforces GPT partition style.
- [ ] **12.12** USB warning appears before formatting.
- [ ] **12.13** ADK missing state disables `Start` navigation through the shell guard.
- [ ] **12.14** ISO creation overlay blocks navigation until completion.
- [ ] **12.15** USB creation overlay blocks navigation until completion.
- [ ] **12.16** Confirm media creation logs remain readable and complete deferred Phase 10 validation **10.12**:
  - [ ] ISO creation logs include start, progress, completion, cancellation, and failure details.
  - [ ] USB creation logs include start, progress, completion, cancellation, and failure details.
  - [ ] Logs are readable in `C:\ProgramData\Foundry\Logs\Foundry.log` without enabling `Verbose`.

## Phase 13: ADK And WinPE Service Integration

**Priority:** high.

**Goal:** port the Windows ADK and WinPE orchestration into the dedicated `ADK` page, shell navigation guard, and blocking operation overlay model.

**Execution note:** implement Phase 13 before the final Phase 12 media UI wiring, because ADK detection, WinPE workspace preparation, `ProgramData` layout, and runtime normalization are prerequisites for reliable ISO/USB creation.

- [ ] **13.1** Port `AdkService`.
  - [ ] **13.1.1** Create the `ADK` page view model with:
    - [ ] ADK installed state.
    - [ ] ADK compatible state.
    - [ ] Installed ADK version.
    - [ ] Required ADK version policy.
    - [ ] WinPE Add-on status.
    - [ ] ISO/USB capability state.
    - [ ] Current ADK operation progress.
    - [ ] Current ADK operation status.
  - [ ] **13.1.2** Keep the `ADK` page actions simple:
    - [ ] Install ADK.
    - [ ] Upgrade ADK.
    - [ ] Refresh status.
    - [ ] Open logs.
  - [ ] **13.1.3** Do not expose a primary uninstall action on the `ADK` page.
  - [ ] **13.1.4** Keep ADK uninstall only as an internal upgrade implementation detail unless a future advanced diagnostics requirement is approved.
  - [ ] **13.1.5** Run Foundry-managed ADK installation with Foundry's own blocking progress overlay.
  - [ ] **13.1.6** Run Foundry-managed ADK upgrade with Foundry's own blocking progress overlay.
  - [ ] **13.1.7** Run ADK and WinPE Add-on setup in silent mode:
    - [ ] `adksetup.exe`.
    - [ ] `adkwinpesetup.exe`.
  - [ ] **13.1.8** Do not show the native Microsoft ADK setup wizard during normal Foundry-managed installation.
  - [ ] **13.1.9** Revalidate official Microsoft ADK download URLs and supported ADK version before implementation.
  - [ ] **13.1.10** Treat the current WPF `10.1.26100.*` compatibility policy as the starting point.
  - [ ] **13.1.11** Update compatibility if the target official ADK version changes before implementation.
- [ ] **13.2** Port WinPE services in dependency order:
  - [ ] Tool resolution.
  - [ ] Process runner.
  - [ ] Build workspace.
  - [ ] Driver catalog.
  - [ ] Driver resolution.
  - [ ] Driver injection.
  - [ ] Image internationalization.
  - [ ] WinRE boot image preparation.
  - [ ] Mounted image asset provisioning.
  - [ ] Mounted image customization.
  - [ ] Local Connect embedding.
  - [ ] Local Deploy embedding.
  - [ ] Workspace preparation.
  - [ ] Media output.
  - [ ] USB media output.
- [ ] **13.3** Preserve bundled 7-Zip assets.
- [ ] **13.4** Preserve embedded PowerShell bootstrap asset.
- [ ] **13.5** Preserve local debug environment variables:
  - [ ] Local Connect enable variable.
  - [ ] Local Connect project path variable.
  - [ ] Local Deploy enable variable.
  - [ ] Local Deploy project path variable.
- [ ] **13.6** Audit and document the filesystem layout contracts before porting the services:
  - [ ] Host authoring data: `C:\ProgramData\Foundry`.
  - [ ] New build workspace root.
  - [ ] New installer/cache root.
  - [ ] New temporary workspace root.
  - [ ] New host-side log root: `C:\ProgramData\Foundry\Logs`.
  - [ ] Boot image runtime: `X:\Foundry`.
  - [ ] USB boot partition.
  - [ ] USB cache partition labeled `Foundry Cache`.
  - [ ] Target Windows temporary deployment root.
- [ ] **13.7** Rename or wrap ambiguous path concepts in `Foundry.Core`:
  - [ ] Distinguish `RuntimeRoot` from `CacheRoot`.
  - [ ] Distinguish host build workspace from WinPE runtime workspace.
  - [ ] Distinguish ISO transient runtime from USB persistent cache.
- [ ] **13.8** Reassess the current `CacheRootPath` behavior where a path ending in `Runtime` writes `OperatingSystem` and `DriverPack` to its parent.
- [ ] **13.9** Normalize the Connect and Deploy runtime cache layout immediately:
  - [ ] Use one application-root convention: `Runtime\<ApplicationName>\<rid>`.
  - [ ] Store Connect at `Runtime\Foundry.Connect\<rid>`.
  - [ ] Store Deploy at `Runtime\Foundry.Deploy\<rid>`.
  - [ ] Update `FoundryBootstrap.ps1` to resolve both applications through the normalized convention.
  - [ ] Update ISO runtime provisioning to write the normalized layout.
  - [ ] Update USB cache provisioning to write the normalized layout.
  - [ ] Update runtime manifest/cache metadata paths if they depend on the old Deploy shape.
- [ ] **13.10** Remove the unnecessary `Foundry.Connect` runtime duplication on USB:
  - [ ] Keep `Foundry.Connect` in the `Foundry Cache` partition.
  - [ ] Keep only minimal boot/bootstrap files on the BOOT partition.
  - [ ] Ensure ISO mode still has the required runtime under `X:\Foundry\Runtime`.
- [ ] **13.11** Remove or explicitly mark obsolete legacy archive contracts:
  - [ ] `Foundry\Seed\Foundry.Connect.zip` appears unused after extracted Connect runtime provisioning.
  - [ ] `Foundry\Seed\Foundry.Deploy.zip` remains a valid local Deploy seed package.
- [ ] **13.12** Fix or remove unused ISO volume-label intent:
  - [ ] `IsoOutputOptions.VolumeLabel` is currently configured by the app but not passed to `MakeWinPEMedia`.
- [ ] **13.13** Implement the new host-side layout directly, with no legacy fallback:
  - [ ] `C:\ProgramData\Foundry\Workspaces\WinPe`.
  - [ ] `C:\ProgramData\Foundry\Workspaces\Iso`.
  - [ ] `C:\ProgramData\Foundry\Cache\OperatingSystems`.
  - [ ] `C:\ProgramData\Foundry\Cache\Installers`.
  - [ ] `C:\ProgramData\Foundry\Cache\Tools`.
  - [ ] `C:\ProgramData\Foundry\Temp`.
  - [ ] `C:\ProgramData\Foundry\Logs`.
- [ ] **13.14** Do not read from or migrate old host folders in application code:
  - [ ] `C:\ProgramData\Foundry\WinPeWorkspace`.
  - [ ] `C:\ProgramData\Foundry\Installers\os`.
  - [ ] `C:\ProgramData\Foundry\Installers\OperatingSystems`.
  - [ ] `C:\ProgramData\Foundry\IsoWorkspace`.
  - [ ] `C:\ProgramData\Foundry\IsoOutputTemp`.
- [ ] **13.15** Add failure-path checks for log relocation:
  - [ ] Startup logs under `X:\Foundry\Logs`.
  - [ ] Deployment session logs under the active workspace.
  - [ ] Target logs under `Windows\Temp\Foundry\Logs`.
- [ ] **13.16** Commit:

```powershell
git commit -m "feat: port winpe orchestration services"
```

**Validation**

- [ ] **13.17** Existing WinPE unit tests pass after moving logic.
- [ ] **13.18** Local debug Connect publish override still works.
- [ ] **13.19** Local debug Deploy publish override still works.
- [ ] **13.20** ISO creation works on a test machine with ADK.
- [ ] **13.21** USB creation works on a disposable test drive.
- [ ] **13.22** Generated ISO/USB media matches the documented layout.
- [ ] **13.23** New host-side `ProgramData` layout is used without old-folder fallback.
- [ ] **13.24** ADK page shows missing state when ADK is absent.
- [ ] **13.25** ADK page shows installed version when ADK is present.
- [ ] **13.26** ADK page shows incompatible state when the version is unsupported.
- [ ] **13.27** ADK install overlay blocks navigation until completion.
- [ ] **13.28** ADK upgrade overlay blocks navigation until completion.
- [ ] **13.29** ADK-compatible state unlocks `General`, `Start`, and `Expert` pages.

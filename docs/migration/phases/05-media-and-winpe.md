# Media And WinPE Phases

## Phase 12: ADK And WinPE Service Integration

**Priority:** high.

**Goal:** port the Windows ADK and WinPE orchestration into the dedicated `ADK` page, shell navigation guard, and blocking operation overlay model.

**Execution note:** implement Phase 12 before the final Phase 13 media UI wiring, because ADK detection, WinPE workspace preparation, `ProgramData` layout, and runtime normalization are prerequisites for reliable ISO/USB creation.

**Recommended implementation split:** Phase 12 is intentionally broad and should be delivered through focused PRs instead of one large branch:

- [ ] **12.A** `feat(adk): add adk status and page integration`.
  - [ ] Scope: ADK/WinPE Add-on detection, installed version, compatibility policy, ADK page state/actions, ADK operation progress, ADK install/upgrade overlay entry points, and shell guard readiness.
  - [ ] Boundary: answer whether Foundry is ready to unlock `General`, `Start`, and `Expert`; do not wire final ISO/USB creation commands here.
  - [ ] Reason: Phase 13 and expert workflows need a reliable ADK readiness state before they can safely expose media or configuration workflows.
- [ ] **12.B** `feat(winpe): port winpe service foundations`.
  - [ ] Scope: tool resolution, process runner, build workspace, driver catalog/resolution/injection, image internationalization, WinRE boot image preparation, mounted image asset provisioning/customization, workspace preparation, and media output service foundations.
  - [ ] Boundary: port service and Core orchestration building blocks; keep final `Start` page command wiring in Phase 13.
  - [ ] Reason: these services can be validated independently from the final WinUI media workflow and should be stable before user-facing ISO/USB execution is added.
- [ ] **12.C** `refactor(runtime): normalize connect deploy runtime layout`.
  - [ ] Scope: normalize `Runtime\Foundry.Connect\<rid>` and `Runtime\Foundry.Deploy\<rid>`, update bootstrap resolution, update ISO runtime provisioning, update USB cache provisioning, remove unnecessary `Foundry.Connect` USB duplication, and preserve local debug Connect/Deploy overrides.
  - [ ] Boundary: touch runtime payload layout and bootstrap assumptions only; do not redesign Connect or Deploy application behavior unless the layout change necessarily flows into them.
  - [ ] Reason: Connect/Deploy runtime layout affects ISO, USB, bootstrap, and downstream compatibility, so it needs a focused PR with explicit validation.
- [ ] **12.D** `feat(winpe): apply programdata and media layout`.
  - [ ] Scope: enforce the new `C:\ProgramData\Foundry` host layout, `X:\Foundry` boot image layout, USB BOOT/cache layout, cache/temp/log placement, no old-folder fallback, failure-path log relocation, and ISO volume-label behavior.
  - [ ] Boundary: make the documented filesystem contract real after ADK readiness, WinPE service foundations, and runtime payload layout are known.
  - [ ] Reason: final layout enforcement should happen after the service and runtime contracts are clear, so old path behavior can be removed directly without compatibility fallback.

**Deferred infrastructure completion:** Phase 12 is also responsible for completing the ADK/WinPE portions of earlier deferred infrastructure work:

- [ ] Complete Phase 6 readiness item **6.8.1** for ADK detection, WinPE Add-on readiness, and ADK-gated startup readiness.
- [ ] Complete Phase 10 logging contract item **10.6.1** for ADK detection and bootstrap payload resolution logs.

- [ ] **12.1** Port `AdkService`.
  - [ ] **12.1.1** Create the `ADK` page view model with:
    - [ ] ADK installed state.
    - [ ] ADK compatible state.
    - [ ] Installed ADK version.
    - [ ] Required ADK version policy.
    - [ ] WinPE Add-on status.
    - [ ] ISO/USB capability state.
    - [ ] Current ADK operation progress.
    - [ ] Current ADK operation status.
  - [ ] **12.1.2** Keep the `ADK` page actions simple:
    - [ ] Install ADK.
    - [ ] Upgrade ADK.
    - [ ] Refresh status.
    - [ ] Open logs from the ADK page diagnostics/action area, not from a dedicated footer navigation item.
  - [ ] **12.1.3** Do not expose a primary uninstall action on the `ADK` page.
  - [ ] **12.1.4** Keep ADK uninstall only as an internal upgrade implementation detail unless a future advanced diagnostics requirement is approved.
  - [ ] **12.1.5** Run Foundry-managed ADK installation with Foundry's own blocking progress overlay.
  - [ ] **12.1.6** Run Foundry-managed ADK upgrade with Foundry's own blocking progress overlay.
  - [ ] **12.1.7** Run ADK and WinPE Add-on setup in silent mode:
    - [ ] `adksetup.exe`.
    - [ ] `adkwinpesetup.exe`.
  - [ ] **12.1.8** Do not show the native Microsoft ADK setup wizard during normal Foundry-managed installation.
  - [ ] **12.1.9** Revalidate official Microsoft ADK download URLs and supported ADK version before implementation.
  - [ ] **12.1.10** Treat the current WPF `10.1.26100.*` compatibility policy as the starting point.
  - [ ] **12.1.11** Update compatibility if the target official ADK version changes before implementation.
- [ ] **12.2** Port WinPE services in dependency order:
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
- [ ] **12.3** Preserve bundled 7-Zip assets.
- [ ] **12.4** Preserve embedded PowerShell bootstrap asset.
- [ ] **12.5** Preserve local debug environment variables:
  - [ ] Local Connect enable variable.
  - [ ] Local Connect project path variable.
  - [ ] Local Deploy enable variable.
  - [ ] Local Deploy project path variable.
- [ ] **12.6** Audit and document the filesystem layout contracts before porting the services:
  - [ ] Host authoring data: `C:\ProgramData\Foundry`.
  - [ ] New build workspace root.
  - [ ] New installer/cache root.
  - [ ] New temporary workspace root.
  - [ ] New host-side log root: `C:\ProgramData\Foundry\Logs`.
  - [ ] Boot image runtime: `X:\Foundry`.
  - [ ] USB boot partition.
  - [ ] USB cache partition labeled `Foundry Cache`.
  - [ ] Target Windows temporary deployment root.
- [ ] **12.7** Rename or wrap ambiguous path concepts in `Foundry.Core`:
  - [ ] Distinguish `RuntimeRoot` from `CacheRoot`.
  - [ ] Distinguish host build workspace from WinPE runtime workspace.
  - [ ] Distinguish ISO transient runtime from USB persistent cache.
- [ ] **12.8** Reassess the current `CacheRootPath` behavior where a path ending in `Runtime` writes `OperatingSystem` and `DriverPack` to its parent.
- [ ] **12.9** Normalize the Connect and Deploy runtime cache layout immediately:
  - [ ] Use one application-root convention: `Runtime\<ApplicationName>\<rid>`.
  - [ ] Store Connect at `Runtime\Foundry.Connect\<rid>`.
  - [ ] Store Deploy at `Runtime\Foundry.Deploy\<rid>`.
  - [ ] Update `FoundryBootstrap.ps1` to resolve both applications through the normalized convention.
  - [ ] Update ISO runtime provisioning to write the normalized layout.
  - [ ] Update USB cache provisioning to write the normalized layout.
  - [ ] Update runtime manifest/cache metadata paths if they depend on the old Deploy shape.
- [ ] **12.10** Remove the unnecessary `Foundry.Connect` runtime duplication on USB:
  - [ ] Keep `Foundry.Connect` in the `Foundry Cache` partition.
  - [ ] Keep only minimal boot/bootstrap files on the BOOT partition.
  - [ ] Ensure ISO mode still has the required runtime under `X:\Foundry\Runtime`.
- [ ] **12.11** Remove or explicitly mark obsolete legacy archive contracts:
  - [ ] `Foundry\Seed\Foundry.Connect.zip` appears unused after extracted Connect runtime provisioning.
  - [ ] `Foundry\Seed\Foundry.Deploy.zip` remains a valid local Deploy seed package.
- [ ] **12.12** Fix or remove unused ISO volume-label intent:
  - [ ] `IsoOutputOptions.VolumeLabel` is currently configured by the app but not passed to `MakeWinPEMedia`.
- [ ] **12.13** Implement the new host-side layout directly, with no legacy fallback:
  - [ ] `C:\ProgramData\Foundry\Workspaces\WinPe`.
  - [ ] `C:\ProgramData\Foundry\Workspaces\Iso`.
  - [ ] `C:\ProgramData\Foundry\Cache\OperatingSystems`.
  - [ ] `C:\ProgramData\Foundry\Cache\Installers`.
  - [ ] `C:\ProgramData\Foundry\Cache\Tools`.
  - [ ] `C:\ProgramData\Foundry\Temp`.
  - [ ] `C:\ProgramData\Foundry\Logs`.
- [ ] **12.14** Do not read from or migrate old host folders in application code:
  - [ ] `C:\ProgramData\Foundry\WinPeWorkspace`.
  - [ ] `C:\ProgramData\Foundry\Installers\os`.
  - [ ] `C:\ProgramData\Foundry\Installers\OperatingSystems`.
  - [ ] `C:\ProgramData\Foundry\IsoWorkspace`.
  - [ ] `C:\ProgramData\Foundry\IsoOutputTemp`.
- [ ] **12.15** Add failure-path checks for log relocation:
  - [ ] Startup logs under `X:\Foundry\Logs`.
  - [ ] Deployment session logs under the active workspace.
  - [ ] Target logs under `Windows\Temp\Foundry\Logs`.
- [ ] **12.16** Commit:

```powershell
git commit -m "feat(winpe): port orchestration services"
```

**Validation**

- [ ] **12.17** Existing WinPE unit tests pass after moving logic.
- [ ] **12.18** Local debug Connect publish override still works.
- [ ] **12.19** Local debug Deploy publish override still works.
- [ ] **12.20** ISO creation works on a test machine with ADK.
- [ ] **12.21** USB creation works on a disposable test drive.
- [ ] **12.22** Generated ISO/USB media matches the documented layout.
- [ ] **12.23** New host-side `ProgramData` layout is used without old-folder fallback.
- [ ] **12.24** ADK page shows missing state when ADK is absent.
- [ ] **12.25** ADK page shows installed version when ADK is present.
- [ ] **12.26** ADK page shows incompatible state when the version is unsupported.
- [ ] **12.27** ADK install overlay blocks navigation until completion.
- [ ] **12.28** ADK upgrade overlay blocks navigation until completion.
- [ ] **12.29** ADK-compatible state unlocks `General`, `Start`, and `Expert` pages.

## Phase 13: General Media Creation Workflow

**Priority:** medium-high after Phase 12 service prerequisites.

**Goal:** port the standard ISO/USB creation workflow into the `Start` page and blocking operation overlay model.

**Prerequisites:** Phase 11 shell/overlay contract and the Phase 12 ADK/WinPE service contract must be available before implementing the final media creation commands.

- [ ] **13.1** Create WinUI view model for media creation on the `Start` page.
- [ ] **13.2** Port state from WPF `MainWindowViewModel`:
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
- [ ] **13.3** Port commands:
  - [ ] Browse ISO path.
  - [ ] Browse custom driver folder.
  - [ ] Refresh USB disks.
  - [ ] Create ISO.
  - [ ] Create USB.
- [ ] **13.4** Implement WinUI warning dialog for destructive USB formatting.
- [ ] **13.5** Use app dispatcher abstraction for UI updates.
- [ ] **13.6** Keep media build service logic in core/app services, not page code-behind.
  - [ ] **13.6.1** Show a final execution summary before ISO or USB creation:
    - [ ] ADK status.
    - [ ] WinPE language.
    - [ ] Architecture.
    - [ ] ISO output path.
    - [ ] USB target.
    - [ ] Runtime payload readiness.
    - [ ] Driver options.
    - [ ] Network validation.
  - [ ] **13.6.2** Run ISO creation inside the blocking operation overlay.
  - [ ] **13.6.3** Run USB creation inside the blocking operation overlay.
  - [ ] **13.6.4** Keep navigation blocked until ISO or USB creation fully completes.
- [ ] **13.7** Commit:

```powershell
git commit -m "feat(media): port creation workflow to winui"
```

**Validation**

- [ ] **13.8** ADK missing state disables media creation.
- [ ] **13.9** Invalid ISO path disables ISO creation.
- [ ] **13.10** No USB candidate disables USB creation.
- [ ] **13.11** ARM64 enforces GPT partition style.
- [ ] **13.12** USB warning appears before formatting.
- [ ] **13.13** ADK missing state disables `Start` navigation through the shell guard.
- [ ] **13.14** ISO creation overlay blocks navigation until completion.
- [ ] **13.15** USB creation overlay blocks navigation until completion.
- [ ] **13.16** Confirm media creation logs remain readable and complete deferred Phase 10 validation **10.12**:
  - [ ] ISO creation logs include start, progress, completion, cancellation, and failure details.
  - [ ] USB creation logs include start, progress, completion, cancellation, and failure details.
  - [ ] Logs are readable in `C:\ProgramData\Foundry\Logs\Foundry.log` without enabling `Verbose`.

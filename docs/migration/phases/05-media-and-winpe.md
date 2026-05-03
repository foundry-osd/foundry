# Media And WinPE Phases

## Phase 12: ADK And WinPE Service Integration

**Priority:** high.

**Goal:** port the Windows ADK and WinPE orchestration into the dedicated `ADK` page, shell navigation guard, and blocking operation overlay model.

**Execution note:** implement Phase 12 before the final Phase 13 media UI wiring, because ADK detection, WinPE workspace preparation, `ProgramData` layout, and runtime normalization are prerequisites for reliable ISO/USB creation.

**Recommended implementation split:** Phase 12 is intentionally broad and should be delivered through focused PRs instead of one large branch:

The `12.A` to `12.D` labels are implementation slices, not extra phase numbers. They are determined by dependency boundaries:

- [x] `12.A` owns user-visible ADK readiness and navigation gating.
- [ ] `12.B` owns pure WinPE service foundations that can be tested without final UI commands.
- [ ] `12.C` owns Connect/Deploy runtime payload layout and bootstrap resolution.
- [ ] `12.D` owns final host/media filesystem layout enforcement after services and runtime paths are stable.

- [x] **12.A** `feat(adk): add adk status and page integration`.
  - [x] Scope: ADK/WinPE Add-on detection, installed version, compatibility policy, ADK page state/actions, ADK operation progress, ADK install/upgrade overlay entry points, and shell guard readiness.
  - [x] Boundary: answer whether Foundry is ready to unlock `General`, `Start`, and `Expert`; do not wire final ISO/USB creation commands here.
  - [x] Reason: Phase 13 and expert workflows need a reliable ADK readiness state before they can safely expose media or configuration workflows.
- [ ] **12.B** `feat(winpe): port winpe service foundations`.
  - [ ] Scope: tool resolution, process runner, build workspace, boot image source strategy, driver catalog/resolution/injection, image internationalization, WinRE boot image preparation, mounted image asset provisioning/customization, workspace preparation, and media output service foundations.
  - [ ] Boundary: port service and Core orchestration building blocks; keep final `Start` page command wiring in Phase 13.
  - [ ] Reason: these services can be validated independently from the final WinUI media workflow and should be stable before user-facing ISO/USB execution is added.
- [ ] **12.C** `refactor(runtime): normalize connect deploy runtime layout`.
  - [ ] Scope: normalize `Runtime\Foundry.Connect\<rid>` and `Runtime\Foundry.Deploy\<rid>`, update bootstrap resolution, update ISO runtime provisioning, update USB cache provisioning, remove unnecessary `Foundry.Connect` USB duplication, preserve local debug Connect/Deploy overrides, and preserve release/download fallback behavior.
  - [ ] Boundary: touch runtime payload layout and bootstrap assumptions only; do not redesign Connect or Deploy application behavior unless the layout change necessarily flows into them.
  - [ ] Reason: Connect/Deploy runtime layout affects ISO, USB, bootstrap, and downstream compatibility, so it needs a focused PR with explicit validation.
- [ ] **12.D** `feat(winpe): apply programdata and media layout`.
  - [ ] Scope: enforce the new `C:\ProgramData\Foundry` host layout, `X:\Foundry` boot image layout, USB BOOT/cache layout, cache/temp/log placement, media secret-key placement, no old-folder fallback, failure-path log relocation, and ISO volume-label behavior.
  - [ ] Boundary: make the documented filesystem contract real after ADK readiness, WinPE service foundations, and runtime payload layout are known.
  - [ ] Reason: final layout enforcement should happen after the service and runtime contracts are clear, so old path behavior can be removed directly without compatibility fallback.

**Deferred infrastructure completion:** Phase 12 is also responsible for completing the ADK/WinPE portions of earlier deferred infrastructure work:

- [x] Complete Phase 6 readiness item **6.8.1** for ADK detection, WinPE Add-on readiness, and ADK-gated startup readiness.
- [ ] Complete Phase 6 readiness item **6.8.1** for USB target service readiness after ADK compatibility is known; keep the final `Start` page refresh command wiring in Phase 13.
- [ ] Complete Phase 10 logging contract item **10.6.1** for ADK detection and bootstrap payload resolution logs.

**WPF reference scenario audit:** before implementing each Phase 12 slice, compare the new WinUI/Core behavior against the archived WPF reference without modifying it:

- [ ] Local Debug scenario:
  - [ ] Preserve Visual Studio/debugger local Connect and Deploy publishing behavior.
  - [ ] Preserve local archive overrides before local project publish.
  - [ ] Preserve local project auto-discovery behavior where practical.
- [ ] Release scenario:
  - [ ] Preserve release asset resolution for Connect and Deploy.
  - [ ] Preserve runtime download fallback from the WinPE bootstrap where applicable.
  - [ ] Preserve SHA256 override checks for runtime archives when provided.
- [ ] Media scenario:
  - [ ] Preserve ISO behavior where required runtime/configuration lives inside `X:\Foundry`.
  - [ ] Preserve USB behavior where persistent runtime/cache data lives on the `Foundry Cache` partition.
  - [ ] Preserve source marker behavior for local versus release-provisioned payloads.
  - [ ] Preserve MakeWinPEMedia non-ASCII path handling by using an ASCII-safe temporary workspace/output path when required and copying the result back to the requested destination.
  - [ ] Preserve PCA2023 `/bootex` capability probing, unsupported-path failure behavior, ISO `/bootex` argument usage, and USB EFI boot file rewriting.
  - [ ] Preserve USB disk safety checks: USB bus, removable media, non-system disk, non-boot disk, and selected disk identity revalidation before formatting.
  - [ ] Preserve USB copy/provision verification for `sources\boot.wim`, `boot\BCD`, architecture-specific EFI boot files, and accepted `robocopy` exit codes.
  - [ ] Preserve idempotent WinPE auto-start wiring by ensuring `startnet.cmd` runs `wpeinit` and invokes `FoundryBootstrap.ps1` exactly once.
  - [ ] Preserve `curl.exe` provisioning into WinPE `System32` for bootstrap download preference/fallback behavior.
- [ ] Boot image source scenario:
  - [ ] Preserve standard `WinPe` boot image creation through ADK workspace tooling.
  - [ ] Preserve `WinReWifi` behavior for Wi-Fi-capable WinRE boot image preparation.
  - [ ] Preserve ESD cache/download/hash validation, image index resolution, `dism /Export-Image`, `winre.wim` extraction, and required Wi-Fi dependency staging.
  - [ ] Preserve WinRE Wi-Fi customization details: remove conflicting `winpeshl.ini` and copy staged `dmcmnutils.dll` and `mdmregistration.dll` into mounted `System32`.
  - [ ] Preserve WinPE bootstrap network/time setup: start `dot3svc`, conditionally start `WlanSvc` only when WinRE Wi-Fi dependencies are present, sync internet date/time when drift exceeds the threshold, and apply timezone from environment, generated Deploy config, geolocation fallback, or UTC.
- [ ] Language and optional component scenario:
  - [ ] Preserve WinPE boot language discovery from the installed ADK `WinPE_OCs` tree.
  - [ ] Preserve base language pack installation before language-specific optional component packages.
  - [ ] Preserve required optional component ordering and non-fatal handling for already-installed or not-applicable packages.
  - [ ] Preserve `dism /Set-AllIntl` and `dism /Set-InputLocale` behavior.

- [x] **12.1** Port `AdkService`.
  - [x] **12.1.1** Create the `ADK` page view model with:
    - [x] ADK installed state.
    - [x] ADK compatible state.
    - [x] Installed ADK version.
    - [x] Required ADK version policy.
    - [x] WinPE Add-on status.
    - [x] ISO/USB capability state.
    - [x] Current ADK operation progress.
    - [x] Current ADK operation status.
  - [x] **12.1.2** Keep the `ADK` page actions simple:
    - [x] Install ADK.
    - [x] Upgrade ADK.
    - [x] Refresh status.
    - [x] Open logs from the ADK page diagnostics/action area, not from a dedicated footer navigation item.
  - [x] **12.1.3** Do not expose a primary uninstall action on the `ADK` page.
  - [x] **12.1.4** Keep ADK uninstall only as an internal upgrade implementation detail unless a future advanced diagnostics requirement is approved.
  - [x] **12.1.5** Run Foundry-managed ADK installation with Foundry's own blocking progress overlay.
  - [x] **12.1.6** Run Foundry-managed ADK upgrade with Foundry's own blocking progress overlay.
  - [x] **12.1.7** Run ADK and WinPE Add-on setup in silent mode:
    - [x] `adksetup.exe`.
    - [x] `adkwinpesetup.exe`.
  - [x] **12.1.8** Do not show the native Microsoft ADK setup wizard during normal Foundry-managed installation.
  - [x] **12.1.9** Revalidate official Microsoft ADK download URLs and supported ADK version before implementation.
  - [x] **12.1.10** Treat the current WPF `10.1.26100.*` compatibility policy as the starting point.
  - [x] **12.1.11** Update compatibility if the target official ADK version changes before implementation.
    - [x] Require `10.1.26100.2454+` for the general Windows 11 24H2/25H2 ADK track.
    - [x] Allow `10.1.28000.1+` for the Windows 11 26H1 Arm64 ADK track.
    - [x] Display that Microsoft recommends applying the latest ADK servicing patch for the detected target track.
  - [x] **12.1.12** Version ADK installer cache files and write downloads atomically so interrupted downloads are not reused.
- [ ] **12.2** Port WinPE services in dependency order:
  - [ ] Tool resolution.
  - [ ] Process runner.
  - [ ] Build workspace.
  - [ ] Boot image source strategy:
    - [ ] Standard `WinPe` path.
    - [ ] `WinReWifi` path.
    - [ ] ESD catalog candidate selection by language, architecture, and OS version.
    - [ ] ESD cache reuse and SHA256 validation.
    - [ ] DISM image index resolution.
    - [ ] DISM image export.
    - [ ] `winre.wim` extraction.
    - [ ] Wi-Fi dependency staging for `dmcmnutils.dll` and `mdmregistration.dll`.
  - [ ] Driver catalog.
  - [ ] Driver resolution.
  - [ ] Wi-Fi supplement driver resolution for the `WinReWifi` path.
  - [ ] Driver injection.
  - [ ] Image internationalization.
  - [ ] WinPE optional component provisioning:
    - [ ] `WinPE-WMI`.
    - [ ] `WinPE-NetFX`.
    - [ ] `WinPE-Scripting`.
    - [ ] `WinPE-PowerShell`.
    - [ ] `WinPE-WinReCfg`.
    - [ ] `WinPE-DismCmdlets`.
    - [ ] `WinPE-StorageWMI`.
    - [ ] `WinPE-Dot3Svc`.
    - [ ] `WinPE-EnhancedStorage`.
    - [ ] Neutral package before localized package when a localized package exists.
    - [ ] Missing base language pack remains fatal.
    - [ ] Already-installed or not-applicable optional components remain non-fatal when safe.
  - [ ] WinRE boot image preparation.
  - [ ] Mounted image asset provisioning.
    - [ ] Always emit complete effective `foundry.deploy.config.json` for generated WinUI media.
    - [ ] Keep `Foundry.Deploy` tolerant when the file is missing for standalone or legacy execution.
    - [ ] Provision `media-secrets.key` only when encrypted embedded runtime secrets are required.
  - [ ] Mounted image customization.
  - [ ] Local Connect embedding.
  - [ ] Local Deploy embedding.
  - [ ] Workspace preparation.
  - [ ] Media output.
  - [ ] USB media output.
- [ ] **12.3** Preserve bundled 7-Zip assets.
- [ ] **12.4** Preserve embedded PowerShell bootstrap asset.
- [ ] **12.4.1** Preserve bootstrap `startnet.cmd` wiring:
  - [ ] Ensure `wpeinit` is present.
  - [ ] Append the `FoundryBootstrap.ps1` invocation only when it is not already present.
  - [ ] Keep the operation idempotent across repeated image customization attempts.
- [ ] **12.4.2** Preserve bootstrap runtime network/time behavior:
  - [ ] Start wired networking services needed by the existing bootstrap.
  - [ ] Start `WlanSvc` only when WinRE Wi-Fi dependencies are present.
  - [ ] Sync internet date/time non-fatally when network probes succeed and clock drift exceeds the configured threshold.
  - [ ] Apply timezone from generated configuration or fallback sources without blocking boot when lookup fails.
- [ ] **12.5** Preserve local debug environment variables:
  - [ ] Local Connect enable variable.
  - [ ] Local Connect project path variable.
  - [ ] Local Connect archive override variable.
  - [ ] Local Deploy enable variable.
  - [ ] Local Deploy project path variable.
  - [ ] Local Deploy archive override variable.
  - [ ] Auto-enable local Connect/Deploy only for debugger-attached developer runs, not for installed production runs.
  - [ ] Prefer explicit local archive override over local project publish.
  - [ ] Fall back to local project publish when no archive override is supplied.
  - [ ] Preserve self-contained single-file local publish output for each selected RID.
- [ ] **12.6** Audit and document the filesystem layout contracts before porting the services:
  - [ ] Host authoring data: `C:\ProgramData\Foundry`.
  - [ ] New build workspace root.
  - [ ] New installer/cache root.
  - [ ] New temporary workspace root.
  - [ ] New host-side log root: `C:\ProgramData\Foundry\Logs`.
  - [ ] Boot image runtime: `X:\Foundry`.
  - [ ] USB boot partition.
  - [ ] USB cache partition labeled `Foundry Cache`.
  - [ ] Media secret key path: `X:\Foundry\Config\Secrets\media-secrets.key`.
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
  - [ ] Correct all Connect and Deploy source retrieval paths after normalization:
    - [ ] Embedded ISO runtime lookup.
    - [ ] USB `Foundry Cache` runtime lookup.
    - [ ] Local debug project publish output lookup.
    - [ ] Local archive override lookup.
    - [ ] GitHub release ZIP lookup.
    - [ ] Existing cache fallback lookup.
    - [ ] Embedded archive fallback lookup when still intentionally supported.
  - [ ] Update ISO runtime provisioning to write the normalized layout.
  - [ ] Update USB cache provisioning to write the normalized layout.
  - [ ] Update runtime manifest/cache metadata paths if they depend on the old Deploy shape.
  - [ ] Preserve bootstrap release tag override variables:
    - [ ] `FOUNDRY_CONNECT_RELEASE_TAG`.
    - [ ] `FOUNDRY_DEPLOY_RELEASE_TAG`.
    - [ ] `FOUNDRY_RELEASE_TAG`.
  - [ ] Preserve bootstrap archive override variables:
    - [ ] `FOUNDRY_CONNECT_ARCHIVE`.
    - [ ] `FOUNDRY_DEPLOY_ARCHIVE`.
    - [ ] `FOUNDRY_CONNECT_ARCHIVE_SHA256`.
    - [ ] `FOUNDRY_DEPLOY_ARCHIVE_SHA256`.
  - [ ] Preserve `curl` download with PowerShell/.NET fallback where the bootstrap still needs runtime download support.
- [ ] **12.9.1** Provision `curl.exe` into WinPE `System32` for architectures where the bootstrap can prefer it before falling back to PowerShell/.NET downloads.
- [ ] **12.10** Remove the unnecessary `Foundry.Connect` runtime duplication on USB:
  - [ ] Keep `Foundry.Connect` in the `Foundry Cache` partition.
  - [ ] Keep only minimal boot/bootstrap files on the BOOT partition.
  - [ ] Ensure ISO mode still has the required runtime under `X:\Foundry\Runtime`.
- [ ] **12.11** Remove or explicitly mark obsolete legacy archive contracts:
  - [ ] `Foundry\Seed\Foundry.Connect.zip` appears unused after extracted Connect runtime provisioning.
  - [ ] `Foundry\Seed\Foundry.Deploy.zip` remains a valid local Deploy seed package.
- [ ] **12.12** Fix or remove unused ISO volume-label intent:
  - [ ] `IsoOutputOptions.VolumeLabel` is currently configured by the app but not passed to `MakeWinPEMedia`.
- [ ] **12.12.1** Preserve MakeWinPEMedia compatibility details:
  - [ ] Probe `/bootex` support before allowing PCA2023 signature mode.
  - [ ] Fail PCA2023 media creation clearly when `/bootex` is unavailable.
  - [ ] Pass `/bootex` for PCA2023 ISO creation.
  - [ ] Rebuild USB EFI boot files from BootEx binaries for PCA2023 USB creation.
  - [ ] Use ASCII-safe temporary ISO workspace/output paths when MakeWinPEMedia cannot handle the requested non-ASCII path directly.
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
  - [ ] USB media must not create or use `Foundry Cache:\Logs`.
  - [ ] Deployment session logs under the active workspace.
  - [ ] Target logs under `Windows\Temp\Foundry\Logs`.
- [ ] **12.16** Commit each Phase 12 slice independently when its validation is complete:
  - [x] **12.16.1** Commit Phase 12.A:

```powershell
git commit -m "feat(adk): add adk status and page integration"
```

  - [ ] **12.16.2** Commit Phase 12.B:

```powershell
git commit -m "feat(winpe): port service foundations"
```

  - [ ] **12.16.3** Commit Phase 12.C:

```powershell
git commit -m "refactor(runtime): normalize connect deploy layout"
```

  - [ ] **12.16.4** Commit Phase 12.D:

```powershell
git commit -m "feat(winpe): apply programdata media layout"
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
- [ ] **12.30** PCA2023 media validation covers both supported and unsupported `/bootex` paths.
- [ ] **12.31** Non-ASCII ISO output path validation confirms the temporary ASCII-safe workaround produces the requested final ISO.
- [ ] **12.32** USB disk safety validation rejects:
  - [ ] Non-USB disks.
  - [ ] Non-removable disks when required.
  - [ ] System disks.
  - [ ] Boot disks.
  - [ ] Selected disks whose identity changed between refresh and execution.
- [ ] **12.33** WinPE customization validation confirms:
  - [ ] `startnet.cmd` contains `wpeinit`.
  - [ ] `startnet.cmd` invokes `FoundryBootstrap.ps1` once.
  - [ ] `curl.exe` is present in mounted `System32` when expected.
  - [ ] WinRE Wi-Fi mode removes conflicting `winpeshl.ini`.
  - [ ] WinRE Wi-Fi dependencies are copied into mounted `System32`.
- [ ] **12.34** Bootstrap script validation confirms:
  - [ ] Wired networking startup is non-fatal.
  - [ ] `WlanSvc` startup is gated by WinRE Wi-Fi dependencies.
  - [ ] Internet date/time synchronization is non-fatal.
  - [ ] Timezone fallback does not block boot.
- [ ] **12.35** USB provisioning validation confirms:
  - [ ] BOOT partition remains minimal.
  - [ ] `Foundry Cache` does not contain or create `Logs`.
  - [ ] `sources\boot.wim`, `boot\BCD`, and architecture-specific EFI boot files are present.
  - [ ] Successful `robocopy` exit codes `0` through `7` are accepted.

## Phase 13: General Media Creation Workflow

**Priority:** medium-high after Phase 12 service prerequisites.

**Goal:** port the standard ISO/USB creation workflow into the `Start` page and blocking operation overlay model.

**Prerequisites:** Phase 11 shell/overlay contract and the Phase 12 ADK/WinPE service contract must be available before implementing the final media creation commands.

**Provisioning boundary:** the final production ISO/USB creation path also depends on Phase 15 for complete `Foundry.Connect` configuration, network asset provisioning, and encrypted embedded Wi-Fi secrets. Phase 13 may build the `Start` page UI and dry-run summaries before Phase 15, but final Create ISO/Create USB commands must stay disabled or clearly marked incomplete until Phase 15 is implemented.

- [ ] **13.1** Create WinUI view model for media creation on the `Start` page.
  - [ ] Keep WinPE boot language selection owned by the `General` page because the WPF source stores it in `GeneralSettings.WinPeLanguage`.
  - [ ] The `Start` page consumes the selected WinPE boot language from `General` and shows it in the final execution summary.
  - [ ] If the `General` page has not yet implemented WinPE boot language selection when media creation is wired, implement the minimal selector there before enabling ISO/USB commands.
  - [ ] Do not confuse WinPE boot language with the expert `Localization` page; that page owns OS deployment language visibility/default/time-zone settings for `Foundry.Deploy`.
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
  - [ ] Selected WinPE boot language from `General`.
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
    - [ ] WinPE boot language from `General`.
    - [ ] Architecture.
    - [ ] ISO output path.
    - [ ] USB target.
    - [ ] Runtime payload readiness.
    - [ ] Driver options.
    - [ ] Network validation.
    - [ ] `Foundry.Connect` provisioning readiness from Phase 15.
    - [ ] Secret envelope/key provisioning readiness when embedded Wi-Fi secrets are required.
    - [ ] Boot image source selection: standard WinPE or Wi-Fi-capable WinRE path.
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
- [ ] **13.17** Confirm the selected WinPE boot language flows from the `General` page into media creation:
  - [ ] Available WinPE boot languages come from the installed ADK `WinPE_OCs` tree.
  - [ ] The selected language controls the language pack and localized optional component packages applied during Phase 12 service execution.
  - [ ] The `Start` page summary shows the selected WinPE boot language before ISO or USB creation.
  - [ ] The expert `Localization` page is not part of this flow.
- [ ] **13.18** Confirm final media command enablement waits for Phase 15 network provisioning readiness:
  - [ ] ISO/USB creation is blocked when required Connect configuration is incomplete.
  - [ ] ISO/USB creation is blocked when encrypted secret-key provisioning is required but unavailable.
  - [ ] ISO mode contains all required runtime/configuration content under `X:\Foundry`.
  - [ ] USB mode keeps BOOT minimal and resolves persistent runtime/cache content from the `Foundry Cache` partition.

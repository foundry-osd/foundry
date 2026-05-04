# Media And WinPE Phases

## Phase 12: ADK And WinPE Service Integration

**Priority:** high.

**Goal:** port the Windows ADK and WinPE orchestration into the dedicated `ADK` page, shell navigation guard, and blocking operation overlay model.

**Execution note:** implement Phase 12 before the final Phase 13 media UI wiring, because ADK detection, WinPE workspace preparation, `ProgramData` layout, and runtime normalization are prerequisites for reliable ISO/USB creation.

**Recommended implementation split:** Phase 12 is intentionally broad and should be delivered through focused PRs instead of one large branch:

The `12.A` to `12.D` labels are implementation slices, not extra phase numbers. They are determined by dependency boundaries:

- [x] `12.A` owns user-visible ADK readiness and navigation gating.
- [x] `12.B` owns pure WinPE service foundations that can be tested without final UI commands.
- [x] `12.C` owns Connect/Deploy runtime payload layout and bootstrap resolution.
- [x] `12.D` owns final host/media filesystem layout enforcement after services and runtime paths are stable.

- [x] **12.A** `feat(adk): add adk status and page integration`.
  - [x] Scope: ADK/WinPE Add-on detection, installed version, compatibility policy, ADK page state/actions, ADK operation progress, ADK install/upgrade overlay entry points, and shell guard readiness.
  - [x] Boundary: answer whether Foundry is ready to unlock `General`, `Start`, and `Expert`; do not wire final ISO/USB creation commands here.
  - [x] Reason: Phase 13 and expert workflows need a reliable ADK readiness state before they can safely expose media or configuration workflows.
- [x] **12.B** `feat(winpe): port winpe service foundations`.
  - [x] Scope: tool resolution, process runner, build workspace, boot image source strategy, driver catalog/resolution/injection, image internationalization, WinRE boot image preparation, mounted image asset provisioning/customization, workspace preparation, and media output service foundations.
  - [x] Boundary: port service and Core orchestration building blocks; keep final `Start` page command wiring in Phase 13.
  - [x] Reason: these services can be validated independently from the final WinUI media workflow and should be stable before user-facing ISO/USB execution is added.
- [x] **12.C** `refactor(runtime): normalize connect deploy runtime layout`.
  - [x] Scope: normalize `Runtime\Foundry.Connect\<rid>` and `Runtime\Foundry.Deploy\<rid>`, update bootstrap resolution, update ISO runtime provisioning, update USB cache provisioning, remove unnecessary `Foundry.Connect` USB duplication, preserve local debug Connect/Deploy overrides, and preserve release/download fallback behavior.
  - [x] Boundary: touch runtime payload layout and bootstrap assumptions only; do not redesign Connect or Deploy application behavior unless the layout change necessarily flows into them.
  - [x] Reason: Connect/Deploy runtime layout affects ISO, USB, bootstrap, and downstream compatibility, so it needs a focused PR with explicit validation.
- [x] **12.D** `feat(winpe): apply programdata and media layout`.
  - [x] Scope: enforce the new `C:\ProgramData\Foundry` host layout, `X:\Foundry` boot image layout, USB BOOT/cache layout, cache/temp/log placement, media secret-key placement, no old-folder fallback, failure-path log relocation, and ISO volume-label behavior.
  - [x] Boundary: make the documented filesystem contract real after ADK readiness, WinPE service foundations, and runtime payload layout are known.
  - [x] Reason: final layout enforcement should happen after the service and runtime contracts are clear, so old path behavior can be removed directly without compatibility fallback.

**Deferred infrastructure completion:** Phase 12 is also responsible for completing the ADK/WinPE portions of earlier deferred infrastructure work:

- [x] Complete Phase 6 readiness item **6.8.1** for ADK detection, WinPE Add-on readiness, and ADK-gated startup readiness.
- [x] Complete Phase 6 readiness item **6.8.1** for USB target service readiness after ADK compatibility is known; keep the final `Start` page refresh command wiring in Phase 13.
- [x] Complete Phase 10 logging contract item **10.6.1** for ADK detection and bootstrap payload resolution logs.

**WPF reference scenario audit:** before implementing each Phase 12 slice, compare the new WinUI/Core behavior against the archived WPF reference without modifying it:

- [x] Local Debug scenario:
  - [x] Preserve Visual Studio/debugger local Connect and Deploy publishing behavior.
  - [x] Preserve local archive overrides before local project publish.
  - [x] Preserve local project auto-discovery behavior where practical.
- [x] Release scenario:
  - [x] Preserve release asset resolution for Connect and Deploy.
  - [x] Preserve runtime download fallback from the WinPE bootstrap where applicable.
  - [x] Preserve SHA256 override checks for runtime archives when provided.
- [x] Media scenario:
  - [x] Preserve ISO behavior where required runtime/configuration lives inside `X:\Foundry`.
  - [x] Preserve USB behavior where persistent runtime/cache data lives on the `Foundry Cache` partition.
  - [x] Preserve source marker behavior for local versus release-provisioned payloads.
  - [x] Preserve MakeWinPEMedia non-ASCII path handling by using an ASCII-safe temporary workspace/output path when required and copying the result back to the requested destination.
  - [x] Preserve PCA2023 `/bootex` capability probing, unsupported-path failure behavior, ISO `/bootex` argument usage, and USB EFI boot file rewriting.
  - [x] Preserve USB disk safety checks: USB bus, removable media, non-system disk, non-boot disk, and selected disk identity revalidation before formatting.
  - [x] Preserve USB copy/provision verification for `sources\boot.wim`, `boot\BCD`, architecture-specific EFI boot files, and accepted `robocopy` exit codes.
  - [x] Preserve idempotent WinPE auto-start wiring by ensuring `startnet.cmd` runs `wpeinit` and invokes `FoundryBootstrap.ps1` exactly once.
  - [x] Preserve `curl.exe` provisioning into WinPE `System32` for bootstrap download preference/fallback behavior.
- [x] Boot image source scenario:
  - [x] Preserve standard `WinPe` boot image creation through ADK workspace tooling.
  - [x] Preserve `WinReWifi` behavior for Wi-Fi-capable WinRE boot image preparation.
  - [x] Preserve ESD cache/download/hash validation, image index resolution, `dism /Export-Image`, `winre.wim` extraction, and required Wi-Fi dependency staging.
  - [x] Preserve WinRE Wi-Fi customization details: remove conflicting `winpeshl.ini` and copy staged `dmcmnutils.dll` and `mdmregistration.dll` into mounted `System32`.
  - [x] Preserve WinPE bootstrap network/time setup: start `dot3svc`, conditionally start `WlanSvc` only when WinRE Wi-Fi dependencies are present, sync internet date/time when drift exceeds the threshold, and apply timezone from environment, generated Deploy config, geolocation fallback, or UTC.
- [x] Language and optional component scenario:
  - [x] Preserve WinPE boot language discovery from the installed ADK `WinPE_OCs` tree.
  - [x] Preserve base language pack installation before language-specific optional component packages.
  - [x] Preserve required optional component ordering and non-fatal handling for already-installed or not-applicable packages.
  - [x] Preserve `dism /Set-AllIntl` and `dism /Set-InputLocale` behavior.

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
    - [x] Do not allow the Windows 11 26H1 Arm64 `10.1.28000.x` ADK track in Foundry.
    - [x] Display that Microsoft recommends applying the latest ADK servicing patch for the detected target track.
  - [x] **12.1.12** Version ADK installer cache files and write downloads atomically so interrupted downloads are not reused.
- [x] **12.2** Port WinPE services in dependency order:
  - [x] Tool resolution.
  - [x] Process runner.
  - [x] Build workspace.
  - [x] Boot image source strategy:
    - [x] Standard `WinPe` path.
    - [x] `WinReWifi` path.
    - [x] ESD catalog candidate selection by language, architecture, and OS version.
    - [x] ESD cache reuse and SHA256 validation.
    - [x] DISM image index resolution.
    - [x] DISM image export.
    - [x] `winre.wim` extraction.
    - [x] Wi-Fi dependency staging for `dmcmnutils.dll` and `mdmregistration.dll`.
  - [x] Driver catalog.
  - [x] Driver resolution.
  - [x] Wi-Fi supplement driver resolution for the `WinReWifi` path.
  - [x] Driver injection.
  - [x] Image internationalization.
  - [x] WinPE boot language discovery from the installed ADK `WinPE_OCs` tree.
  - [x] WinPE optional component provisioning:
    - [x] `WinPE-WMI`.
    - [x] `WinPE-NetFX`.
    - [x] `WinPE-Scripting`.
    - [x] `WinPE-PowerShell`.
    - [x] `WinPE-WinReCfg`.
    - [x] `WinPE-DismCmdlets`.
    - [x] `WinPE-StorageWMI`.
    - [x] `WinPE-Dot3Svc`.
    - [x] `WinPE-EnhancedStorage`.
    - [x] Neutral package before localized package when a localized package exists.
    - [x] Missing base language pack remains fatal.
    - [x] Already-installed or not-applicable optional components remain non-fatal when safe.
  - [x] WinRE boot image preparation.
  - [x] Mounted image asset provisioning.
    - [x] Always emit complete effective `foundry.deploy.config.json` for generated WinUI media.
    - [x] Keep `Foundry.Deploy` tolerant when the file is missing for standalone or legacy execution.
    - [x] Provision `media-secrets.key` only when encrypted embedded runtime secrets are required.
  - [x] Mounted image customization.
  - [x] Runtime payload embedding is deferred to focused Phase 12.C layout work.
  - [x] Workspace preparation.
  - [x] ISO media output foundation.
  - [x] USB media output.
- [x] **12.3** Preserve bundled 7-Zip assets.
- [x] **12.4** Preserve embedded PowerShell bootstrap asset.
- [x] **12.4.1** Preserve bootstrap `startnet.cmd` wiring:
  - [x] Ensure `wpeinit` is present.
  - [x] Append the `FoundryBootstrap.ps1` invocation only when it is not already present.
  - [x] Keep the operation idempotent across repeated image customization attempts.
- [x] **12.4.2** Preserve bootstrap runtime network/time behavior:
  - [x] Start wired networking services needed by the existing bootstrap.
  - [x] Start `WlanSvc` only when WinRE Wi-Fi dependencies are present.
  - [x] Sync internet date/time non-fatally when network probes succeed and clock drift exceeds the configured threshold.
  - [x] Apply timezone from generated configuration or fallback sources without blocking boot when lookup fails.
- [x] **12.5** Preserve local debug environment variables:
  - [x] Local Connect enable variable.
  - [x] Local Connect project path variable.
  - [x] Local Connect archive override variable.
  - [x] Local Deploy enable variable.
  - [x] Local Deploy project path variable.
  - [x] Local Deploy archive override variable.
  - [x] Auto-enable local Connect/Deploy only for debugger-attached developer runs, not for installed production runs.
  - [x] Prefer explicit local archive override over local project publish.
  - [x] Fall back to local project publish when no archive override is supplied.
  - [x] Preserve self-contained single-file local publish output for each selected RID.
- [x] **12.6** Audit and document the filesystem layout contracts before porting the services:
  - [x] Host authoring data: `C:\ProgramData\Foundry`.
  - [x] New build workspace root.
  - [x] New installer/cache root.
  - [x] New temporary workspace root.
  - [x] New host-side log root: `C:\ProgramData\Foundry\Logs`.
  - [x] Boot image runtime: `X:\Foundry`.
  - [x] USB boot partition.
  - [x] USB cache partition labeled `Foundry Cache`.
  - [x] Media secret key path: `X:\Foundry\Config\Secrets\media-secrets.key`.
  - [x] Target Windows temporary deployment root.
- [x] **12.7** Rename or wrap ambiguous path concepts in `Foundry.Core`:
  - [x] Distinguish `RuntimeRoot` from `CacheRoot`.
  - [x] Distinguish host build workspace from WinPE runtime workspace.
  - [x] Distinguish ISO transient runtime from USB persistent cache for Connect/Deploy runtime payload provisioning.
- [x] **12.8** Reassess the current `CacheRootPath` behavior where a path ending in `Runtime` writes `OperatingSystem` and `DriverPack` to its parent.
- [x] **12.9** Normalize the Connect and Deploy runtime cache layout immediately:
  - [x] Use one application-root convention: `Runtime\<ApplicationName>\<rid>`.
  - [x] Store Connect at `Runtime\Foundry.Connect\<rid>`.
  - [x] Store Deploy at `Runtime\Foundry.Deploy\<rid>`.
  - [x] Update `FoundryBootstrap.ps1` to resolve both applications through the normalized convention.
  - [x] Correct all Connect and Deploy source retrieval paths after normalization:
    - [x] Embedded ISO runtime lookup.
    - [x] USB `Foundry Cache` runtime lookup.
    - [x] Local debug project publish output lookup.
    - [x] Local archive override lookup.
    - [x] GitHub release ZIP lookup.
    - [x] Existing cache fallback lookup.
    - [x] Embedded archive fallback lookup when still intentionally supported.
  - [x] Update ISO runtime provisioning to write the normalized layout.
  - [x] Update USB cache provisioning to write the normalized layout.
  - [x] Update runtime manifest/cache metadata paths if they depend on the old Deploy shape; no runtime manifest path currently exists in Core.
  - [x] Preserve bootstrap release tag override variables:
    - [x] `FOUNDRY_CONNECT_RELEASE_TAG`.
    - [x] `FOUNDRY_DEPLOY_RELEASE_TAG`.
    - [x] `FOUNDRY_RELEASE_TAG`.
  - [x] Preserve bootstrap archive override variables:
    - [x] `FOUNDRY_CONNECT_ARCHIVE`.
    - [x] `FOUNDRY_DEPLOY_ARCHIVE`.
    - [x] `FOUNDRY_CONNECT_ARCHIVE_SHA256`.
    - [x] `FOUNDRY_DEPLOY_ARCHIVE_SHA256`.
  - [x] Preserve `curl` download with PowerShell/.NET fallback where the bootstrap still needs runtime download support.
- [x] **12.9.1** Provision `curl.exe` into WinPE `System32` for architectures where the bootstrap can prefer it before falling back to PowerShell/.NET downloads.
- [x] **12.10** Remove the unnecessary `Foundry.Connect` runtime duplication on USB:
  - [x] Keep `Foundry.Connect` in the `Foundry Cache` partition.
  - [x] Keep only minimal boot/bootstrap files on the BOOT partition.
  - [x] Ensure ISO mode still has the required runtime under `X:\Foundry\Runtime`.
- [x] **12.11** Remove or explicitly mark obsolete legacy archive contracts:
  - [x] `Foundry\Seed\Foundry.Connect.zip` appears unused after extracted Connect runtime provisioning.
  - [x] `Foundry\Seed\Foundry.Deploy.zip` remains a valid local Deploy seed package.
- [x] **12.12** Fix or remove unused ISO volume-label intent:
  - [x] No active WinUI service contract exposes an ISO volume-label setting. The archived WPF value was not passed to `MakeWinPEMedia`, so Phase 12.D keeps the generated-media contract explicit without carrying unused volume-label behavior forward.
- [x] **12.12.1** Preserve MakeWinPEMedia compatibility details:
  - [x] Probe `/bootex` support before allowing PCA2023 signature mode.
  - [x] Fail PCA2023 media creation clearly when `/bootex` is unavailable.
  - [x] Pass `/bootex` for PCA2023 ISO creation.
  - [x] Rebuild USB EFI boot files from BootEx binaries for PCA2023 USB creation.
  - [x] Use ASCII-safe temporary ISO workspace/output paths when MakeWinPEMedia cannot handle the requested non-ASCII path directly.
- [x] **12.13** Implement the new host-side layout directly, with no legacy fallback:
  - [x] `C:\ProgramData\Foundry\Workspaces\WinPe`.
  - [x] `C:\ProgramData\Foundry\Workspaces\Iso`.
  - [x] `C:\ProgramData\Foundry\Cache\OperatingSystems`.
  - [x] `C:\ProgramData\Foundry\Cache\Installers`.
  - [x] `C:\ProgramData\Foundry\Cache\Tools`.
  - [x] `C:\ProgramData\Foundry\Temp`.
  - [x] `C:\ProgramData\Foundry\Logs`.
- [x] **12.14** Do not read from or migrate old host folders in application code:
  - [x] `C:\ProgramData\Foundry\WinPeWorkspace`.
  - [x] `C:\ProgramData\Foundry\Installers\os`.
  - [x] `C:\ProgramData\Foundry\Installers\OperatingSystems`.
  - [x] `C:\ProgramData\Foundry\IsoWorkspace`.
  - [x] `C:\ProgramData\Foundry\IsoOutputTemp`.
- [x] **12.15** Add failure-path checks for log relocation:
  - [x] Startup logs under `X:\Foundry\Logs`.
  - [x] USB media must not create or use `Foundry Cache:\Logs`.
  - [x] Deployment session logs under the active workspace.
  - [x] Target logs under `Windows\Temp\Foundry\Logs`.
- [x] **12.16** Commit each Phase 12 slice independently when its validation is complete:
  - [x] **12.16.1** Commit Phase 12.A:

```powershell
git commit -m "feat(adk): add adk status and page integration"
```

  - [x] **12.16.2** Commit Phase 12.B:

```powershell
git commit -m "feat(winpe): port service foundations"
```

  - [x] **12.16.3** Commit Phase 12.C:

```powershell
git commit -m "refactor(runtime): normalize connect deploy layout"
```

  - [x] **12.16.4** Commit Phase 12.D:

```powershell
git commit -m "feat(winpe): apply programdata media layout"
```

**Validation**

- [x] **12.17** Existing WinPE unit tests pass after moving logic.
- [x] **12.17.1** Split Phase 12 manual validation between service-level checks that are testable now and final `Start` page workflow checks that must wait for Phase 13.

**Manual validation available during Phase 12**

- [x] **12.18** Local debug Connect publish override still works.
- [x] **12.19** Local debug Deploy publish override still works.
- [x] **12.23** New host-side `ProgramData` layout is used without old-folder fallback.

**Manual validation deferred to Phase 13 Start page workflow**

- [ ] **12.20** ISO creation works on a test machine with ADK through the final `Start` page command.
- [ ] **12.21** USB creation works on a disposable test drive through the final `Start` page command.
- [ ] **12.22** Generated ISO/USB media matches the documented layout from the final `Start` page workflow.

**ADK and WinPE service validation completed in Phase 12**

- [x] **12.24** ADK page shows missing state when ADK is absent.
- [x] **12.25** ADK page shows installed version when ADK is present.
- [x] **12.26** ADK page shows incompatible state when the version is unsupported.
- [x] **12.27** ADK install overlay blocks navigation until completion.
- [x] **12.28** ADK upgrade overlay blocks navigation until completion.
- [x] **12.29** ADK-compatible state unlocks `General`, `Start`, and `Expert` pages.
- [x] **12.30** PCA2023 media validation covers both supported and unsupported `/bootex` paths.
- [x] **12.31** Non-ASCII ISO workspace/output path validation confirms the temporary ASCII-safe workaround produces the requested final ISO.
- [x] **12.32** USB disk safety validation rejects:
  - [x] Non-USB disks.
  - [x] Non-removable disks when required.
  - [x] System disks.
  - [x] Boot disks.
  - [x] Selected disks whose identity changed between refresh and execution.
- [x] **12.33** WinPE customization validation confirms:
  - [x] `startnet.cmd` contains `wpeinit`.
  - [x] `startnet.cmd` invokes `FoundryBootstrap.ps1` once.
  - [x] `curl.exe` is present in mounted `System32` when expected.
  - [x] Bundled 7-Zip tools are copied into `X:\Foundry\Tools\7zip`.
  - [x] WinRE Wi-Fi mode removes conflicting `winpeshl.ini`.
  - [x] WinRE Wi-Fi dependencies are copied into mounted `System32`.
- [x] **12.34** Bootstrap script validation confirms:
  - [x] Wired networking startup is non-fatal.
  - [x] `WlanSvc` startup is gated by WinRE Wi-Fi dependencies.
  - [x] Internet date/time synchronization is non-fatal.
  - [x] Timezone fallback does not block boot.
- [x] **12.35** USB provisioning validation confirms:
  - [x] BOOT partition remains minimal.
  - [x] `Foundry Cache` does not contain or create `Logs`.
  - [x] `sources\boot.wim`, `boot\BCD`, and architecture-specific EFI boot files are present.
  - [x] Successful `robocopy` exit codes `0` through `7` are accepted.

## Phase 13: Start Page Media Preflight Workflow

**Priority:** medium-high after Phase 12 service prerequisites.

**Goal:** port the standard media configuration surface into the `Start` page, wire readiness/preflight checks, and produce clear dry-run summaries without enabling destructive final ISO/USB execution yet.

**Prerequisites:** Phase 11 shell/overlay contract and the Phase 12 ADK/WinPE service contract must be available before implementing the `Start` page media workflow.

**Provisioning boundary:** the final production ISO/USB creation path also depends on Phase 14 for complete `Foundry.Deploy` configuration generation and Phase 15 for complete `Foundry.Connect` configuration, network asset provisioning, and encrypted embedded Wi-Fi secrets. Phase 13 builds the `Start` page UI, readiness checks, USB discovery, and dry-run summaries. Final Create ISO/Create USB execution remains disabled or explicitly marked incomplete until the final enablement PR after Phases 14 and 15.

- [x] **13.1** Create WinUI view model for media preflight on the `Start` page.
  - [x] Keep WinPE boot language selection owned by the `General` page because the WPF source stores it in `GeneralSettings.WinPeLanguage`.
  - [x] The `Start` page consumes the selected WinPE boot language from `General` and shows it in the dry-run summary.
  - [x] If the `General` page has not yet implemented WinPE boot language selection when media preflight is wired, implement the minimal selector there before enabling dry-run summaries.
  - [x] Do not confuse WinPE boot language with the expert `Localization` page; that page owns OS deployment language visibility/default/time-zone settings for `Foundry.Deploy`.
- [x] **13.2** Port state from WPF `MainWindowViewModel`:
  - [x] ISO output path.
  - [x] Architecture.
  - [x] CA 2023 signature mode.
  - [x] USB partition style.
  - [x] USB format mode.
  - [x] Dell driver inclusion.
  - [x] HP driver inclusion.
  - [x] Custom driver directory.
  - [x] Selected USB disk.
  - [x] Selected WinPE boot language from `General`.
- [x] **13.3** Port commands:
  - [x] Browse ISO path.
  - [x] Browse custom driver folder.
  - [x] Refresh USB disks manually from the `Start` page.
  - [x] Refresh USB disks automatically when the `Start` page loads and ADK is compatible.
  - [x] Generate ISO dry-run summary.
  - [x] Generate USB dry-run summary.
  - [x] Keep final Create ISO/Create USB execution disabled until the final media enablement PR.
- [x] **13.4** Prepare the future WinUI destructive USB warning dialog contract in the dry-run flow.
  - [x] Show disk number, friendly name, and size in the USB target summary.
  - [x] Document that the future final USB command must default to cancel/no.
  - [x] Do not perform destructive formatting in Phase 13.
- [x] **13.5** Use app dispatcher abstraction for UI updates.
- [x] **13.6** Keep media preflight and future media build orchestration in app/core services, not page code-behind.
  - [x] **13.6.1** Show a dry-run execution summary before ISO or USB creation can be enabled:
    - [x] ADK status.
    - [x] WinPE boot language from `General`.
    - [x] Architecture.
    - [x] ISO output path.
    - [x] USB target.
    - [x] Runtime payload readiness.
    - [x] Driver options.
    - [x] Network validation.
    - [x] `Foundry.Connect` provisioning readiness from Phase 15.
    - [x] Secret envelope/key provisioning readiness when embedded Wi-Fi secrets are required.
    - [x] Boot image source selection: standard WinPE or Wi-Fi-capable WinRE path.
  - [x] **13.6.2** Map final ISO creation requirements to a future operation overlay contract without enabling execution.
  - [x] **13.6.3** Map final USB creation requirements to a future operation overlay contract without enabling execution.
  - [x] **13.6.4** Keep final navigation-blocking ISO/USB execution validation deferred until final media command enablement.
- [x] **13.6.5** Complete Phase 6 readiness item **6.8.1** for USB target service readiness after ADK compatibility is known.
- [x] **13.7** Commit:

```powershell
git commit -m "feat(media): add start page preflight workflow"
```

**Validation**

- [ ] **13.8** ADK missing state disables media dry-run summaries and final media commands.
- [ ] **13.9** Invalid ISO path disables ISO dry-run summary and final ISO command.
- [ ] **13.10** No USB candidate disables USB dry-run summary and final USB command.
- [ ] **13.11** ARM64 enforces GPT partition style.
- [ ] **13.12** USB warning contract is present but no destructive formatting runs in Phase 13.
- [ ] **13.13** ADK missing state disables `Start` navigation through the shell guard.
- [ ] **13.14** ISO dry-run summary clearly shows that final ISO execution is deferred until Deploy/Connect provisioning is complete.
- [ ] **13.15** USB dry-run summary clearly shows that final USB execution is deferred until Deploy/Connect provisioning is complete.
- [ ] **13.16** Confirm media preflight logs remain readable and partially complete deferred Phase 10 validation **10.12**:
  - [ ] ISO preflight logs include readiness, selected options, and blocking reasons.
  - [ ] USB preflight logs include readiness, selected options, selected disk identity, and blocking reasons.
  - [ ] Logs are readable in `C:\ProgramData\Foundry\Logs\Foundry.log` without enabling `Verbose`.
- [ ] **13.17** Confirm the selected WinPE boot language flows from the `General` page into media creation:
  - [ ] Available WinPE boot languages come from the installed ADK `WinPE_OCs` tree.
  - [ ] The selected language appears in the `Start` page dry-run summary.
  - [ ] The selected language is mapped to the Phase 12 service option that will control language pack and localized optional component package application during final execution.
  - [ ] The expert `Localization` page is not part of this flow.
- [ ] **13.18** Confirm final media command enablement waits for Phase 14 Deploy configuration and Phase 15 Connect/network provisioning readiness:
  - [ ] ISO/USB creation is blocked when required Connect configuration is incomplete.
  - [ ] ISO/USB creation is blocked when required Deploy configuration is incomplete.
  - [ ] ISO/USB creation is blocked when encrypted secret-key provisioning is required but unavailable.
  - [ ] Final ISO/USB execution remains disabled in Phase 13 even when the dry-run summary is valid.

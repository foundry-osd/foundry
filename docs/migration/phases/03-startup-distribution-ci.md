# Startup, Distribution, And CI Phases

## Phase 6: WinUI Startup, Hosting, DI, And Exception Handling

**Priority:** high.

**Goal:** replace prototype startup with a production startup model close to the current WPF app.

- [x] **6.1** Use a Host-based startup pattern:
  - [x] Use `Host.CreateApplicationBuilder` for application startup.
  - [x] Keep startup composition out of `App.xaml.cs`.
  - [x] Use direct `ServiceCollection` only for isolated tests or minimal design-time helpers.
- [x] **6.2** Register global exception handlers:
  - [x] `AppDomain.CurrentDomain.UnhandledException`.
  - [x] `TaskScheduler.UnobservedTaskException`.
  - [x] WinUI `Application.UnhandledException`.
- [x] **6.3** Initialize logging before the WinUI window is created.
- [x] **6.4** Ensure Velopack startup hook runs before update checks.
- [x] **6.5** Ensure Windows App SDK runtime initialization is understood for the selected packaging mode.
  - [x] **6.5.1** Preserve the application-level administrator requirement with a WinUI app manifest equivalent to WPF `requireAdministrator`.
  - [x] **6.5.2** Do not introduce per-operation elevation during this migration.
- [x] **6.6** Move service registration out of `App.xaml.cs` into:
  - [x] `DependencyInjection\ServiceCollectionExtensions.cs`.
- [x] **6.7** Register:
  - [x] `MainWindow`.
  - [x] Main shell view model.
  - [x] Page view models.
  - [x] Core services.
  - [x] WinUI shell services.
  - [x] Logging.
  - [x] Localization.
  - [x] Update service.
  - [x] Internal `IAppSettingsService`.
  - [x] Shell navigation guard service.
  - Note: localization and update service boundaries are registered; concrete `.resw` migration and Velopack update checks remain in later dedicated phases.
- [x] **6.8** Remove DevWinUI placeholder update metadata.
- [ ] **6.8.1** Implement the startup readiness sequence:
  - [x] Initialize logging, dependency injection, and Velopack startup hooks.
  - [ ] Detect ADK and WinPE Add-on readiness.
  - [x] Apply `AdkBlocked` or `Ready` shell navigation state.
  - [ ] Refresh USB targets only after ADK is compatible.
  - [x] Run update checks after readiness initialization.
  - [x] Ensure startup update checks do not block app usage.
  - Note: ADK detection, USB refresh, and real update checks are intentionally left to the ADK/media and Velopack phases.
- [x] **6.9** Commit:

```powershell
git commit -m "refactor: add winui application startup composition"
```

**Validation**

- [x] **6.10** Start app in Debug.
- [x] **6.11** Start app in Release.
- [x] **6.12** Confirm logs are created on startup.
- [x] **6.13** Confirm an unhandled exception is logged during a controlled debug test.

Note: manual launch validation is kept unchecked until a local Visual Studio run because the WinUI executable now requests administrator elevation.

## Phase 7: Velopack Distribution And Update Flow

**Priority:** high.

**Goal:** replace the old GitHub-release-only update check with real install/update behavior.

- [x] **7.1** Use the decided distribution mode:
  - [x] Primary installer: Velopack `.msi`.
  - [x] Install location: `PerMachine`.
  - [x] App packaging model: unpackaged WinUI with `WindowsPackageType=None`.
  - [x] Update feed: GitHub Releases.
  - [x] Architectures: `win-x64` and `win-arm64`.
  - [x] No x86 installer or update channel.
  - Note: Velopack package channels are runtime-specific (`win-x64` and `win-arm64`) to prevent release asset conflicts. The user-facing update preference remains `stable`.
- [x] **7.2** Define Velopack package identity:
  - [x] Pack ID.
  - [x] Display name.
  - [x] Main executable.
  - [x] Shortcut behavior.
  - [x] Install scope.
- [x] **7.3** Add app startup integration:
  - [x] `VelopackApp.Build().Run()`.
  - [x] First-run callback if needed.
  - [x] Restart-after-update handling.
  - Note: no custom first-run callback is required at this stage; the startup hook is in the real WinUI entry point.
- [x] **7.4** Replace or rewrite `ApplicationUpdateService`:
  - [x] Check for updates using `UpdateManager`.
  - [x] Present Velopack release notes.
  - [x] Download update.
  - [x] Apply update.
  - [x] Restart app when required.
  - [x] Skip update checks in Visual Studio debug sessions.
- [x] **7.5** Do not port the WPF `FlowDocument` release-notes renderer 1:1.
  - [x] Use a simple WinUI release-notes view or simplified text.
  - [x] Keep GitHub release note display only if it adds value beyond Velopack release notes.
- [x] **7.6** Update app settings page:
  - [x] Current version.
  - [x] Update channel/feed.
  - [x] Manual check button.
  - [x] Download/install state.
  - [x] Failure message.
  - [x] Persist update preference, feed, or channel through `IAppSettingsService` when needed.
- [x] **7.7** Use both startup and manual update checks:
  - [x] Startup check runs after app readiness initialization.
  - [x] Startup check must not block normal app usage.
  - [x] Manual check is available from Settings/About update UI.
- [x] **7.8** Add release artifact naming contract:
  - [x] `FoundrySetup-x64.msi`.
  - [x] `FoundrySetup-arm64.msi`.
  - [x] Velopack release metadata files.
- [x] **7.9** Use Velopack CLI directly for MSI packaging:

```powershell
vpk pack --msi --instLocation PerMachine --packId Foundry --packVersion <YY.M.D-build.Build> --packDir <publish-dir> --mainExe Foundry.exe --channel <win-x64|win-arm64>
```

- [x] **7.10** Do not add or maintain a dedicated WiX project for Foundry.
- [x] **7.11** Preserve `Foundry.Connect` and `Foundry.Deploy` ZIP release assets.
- [x] **7.11.1** Replace old main `Foundry-x64.exe` and `Foundry-arm64.exe` release assets with Velopack package/MSI artifacts.
  - Confirmed by Phase 8 draft release `v26.5.1.1`: Foundry is published as Velopack `.nupkg`, setup `.exe`, and `.msi` assets; the old single-file Foundry assets are absent.
- [x] **7.12** Commit:

```powershell
git commit -m "feat: add velopack distribution flow"
```

**Validation**

- [x] **7.13** Publish WinUI app for `win-x64`.
- [x] **7.14** Run `vpk pack --msi --instLocation PerMachine` for `win-x64`.
- [x] **7.15** Install locally.
- [x] **7.16** Launch installed app.
- [x] **7.17** Confirm first-run path works.
- [x] **7.18** Confirm manual update check handles no-update state.
- [ ] **7.19** Repeat for `win-arm64` on ARM64 runner or machine.
  - Note: `win-arm64` package generation is validated locally; install/run validation remains for an ARM64 machine or runner.

## Phase 8: GitHub Actions And Release Workflow Migration

**Priority:** high.

**Goal:** make CI and release automation match the new project structure and packaging model.

- [x] **8.1** Update `.github\workflows\ci.yml`.
- [x] **8.2** Restore `src\Foundry.slnx`.
- [x] **8.3** Build matrix:
  - [x] `x64` on `windows-latest`.
  - [x] `ARM64` on `windows-11-arm`.
- [x] **8.4** Confirm WinUI build works on both runners.
- [x] **8.5** Confirm WPF `Foundry.Connect` and `Foundry.Deploy` still build.
- [x] **8.6** Confirm tests run:
  - [x] `Foundry.Core.Tests`.
  - [x] `Foundry.App.Tests` only if it exists.
  - [x] `Foundry.Connect.Tests` when unchanged or when impacted by shared changes.
  - [x] `Foundry.Deploy.Tests` when unchanged or when impacted by shared changes.
- [x] **8.7** Update `.github\workflows\release.yml`.
- [x] **8.8** Keep manual dispatch during migration.
- [x] **8.9** Keep scheduled release disabled until final cutover.
- [x] **8.10** Replace old single-file `Foundry-x64.exe` and `Foundry-arm64.exe` release output with Velopack package output.
- [x] **8.11** Continue publishing `Foundry.Connect` ZIPs.
- [x] **8.12** Continue publishing `Foundry.Deploy` ZIPs.
- [x] **8.13** Ensure workflow installs required tools:
  - [x] `.NET 10 SDK`.
  - [x] Velopack CLI.
- [x] **8.14** Do not add direct WiX build steps unless Velopack documentation later requires them explicitly.
- [x] **8.15** Keep date-based release versioning.
- [x] **8.16** Keep GitHub release tags in the existing visible format:
  - [x] Tag format: `vYY.M.D.Build`.
  - [x] Example: `v26.4.30.1`.
- [x] **8.17** Map GitHub release tag to Velopack package version:
  - [x] `vYY.M.D.Build` maps to `YY.M.D-build.Build`.
  - [x] Example: `v26.4.30.1` maps to `26.4.30-build.1`.
  - [x] Example: `v26.4.30.2` maps to `26.4.30-build.2`.
- [x] **8.17.1** Apply version values consistently:
  - [x] `Version`: `YY.M.D.Build`.
  - [x] `AssemblyVersion`: `YY.M.D.Build`.
  - [x] `FileVersion`: `YY.M.D.Build`.
  - [x] `InformationalVersion`: `YY.M.D.Build`.
  - [x] Velopack `--packVersion`: `YY.M.D-build.Build`.
- [x] **8.17.2** Validate Velopack ordering for same-day builds before finalizing the release workflow:
  - [x] Confirm `26.4.30-build.2` is treated as newer than `26.4.30-build.1`.
  - [x] Confirm update detection works for the target stable channel with the `-build.` prerelease-style suffix.
  - [x] If Velopack rejects this ordering, stop and choose a different date-based SemVer2 mapping before cutover.
- [x] **8.18** Commit:

```powershell
git commit -m "ci: update workflows for winui packaging"
```

**Validation**

- [x] **8.19** Run CI on PR to `feat/winui-migration`.
- [x] **8.20** Run manual release on a test branch or draft release tag.
- [x] **8.21** Confirm artifacts are uploaded.
- [x] **8.22** Confirm release notes are usable by Velopack.
- [x] **8.23** Confirm Velopack update detection handles `YY.M.D-build.Build` ordering correctly.

**Phase 8 notes**

- `.github\workflows\ci.yml` already matched the expected WinUI migration matrix; Phase 8 reviewed it and kept it unchanged.
- Local validation confirmed `x64` and `ARM64` Release builds, x64 test execution, and Velopack package generation for both runtimes.
- GitHub Actions confirmed CI on `windows-latest` and `windows-11-arm` in PR #117.
- Draft release `v26.5.1.1` completed successfully from `feat/winui-migration` and uploaded Foundry Velopack assets plus `Foundry.Connect` and `Foundry.Deploy` ZIPs.
- The downloaded `Foundry-26.5.1-build.1-win-x64-full.nupkg` contains generated GitHub release notes, including the Phase 8 PR.
- SemVer comparison with NuGet.Versioning `VersionRelease` confirms `26.5.1-build.2` sorts newer than `26.5.1-build.1`.
- Published releases `v26.5.1.1` and `v26.5.1.2` completed successfully from `feat/winui-migration`.
- Velopack update detection was validated with `TestVelopackLocator`: a simulated installed `Foundry` `26.5.1-build.1` on channel `win-x64` detects `26.5.1-build.2` from GitHub Releases.

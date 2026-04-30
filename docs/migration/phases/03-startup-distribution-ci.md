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
  - [ ] Run update checks after readiness initialization.
  - [ ] Ensure startup update checks do not block app usage.
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

- [ ] **7.1** Use the decided distribution mode:
  - [ ] Primary installer: Velopack `.msi`.
  - [ ] Install location: `PerMachine`.
  - [ ] App packaging model: unpackaged WinUI with `WindowsPackageType=None`.
  - [ ] Update feed: GitHub Releases.
  - [ ] Architectures: `win-x64` and `win-arm64`.
  - [ ] No x86 installer or update channel.
- [ ] **7.2** Define Velopack package identity:
  - [ ] Pack ID.
  - [ ] Display name.
  - [ ] Main executable.
  - [ ] Shortcut behavior.
  - [ ] Install scope.
- [ ] **7.3** Add app startup integration:
  - [ ] `VelopackApp.Build().Run()`.
  - [ ] First-run callback if needed.
  - [ ] Restart-after-update handling.
- [ ] **7.4** Replace or rewrite `ApplicationUpdateService`:
  - [ ] Check for updates using `UpdateManager`.
  - [ ] Present Velopack release notes.
  - [ ] Download update.
  - [ ] Apply update.
  - [ ] Restart app when required.
  - [ ] Skip update checks in Visual Studio debug sessions.
- [ ] **7.5** Do not port the WPF `FlowDocument` release-notes renderer 1:1.
  - [ ] Use a simple WinUI release-notes view or simplified text.
  - [ ] Keep GitHub release note display only if it adds value beyond Velopack release notes.
- [ ] **7.6** Update app settings page:
  - [ ] Current version.
  - [ ] Update channel/feed.
  - [ ] Manual check button.
  - [ ] Download/install state.
  - [ ] Failure message.
  - [ ] Persist update preference, feed, or channel through `IAppSettingsService` when needed.
- [ ] **7.7** Use both startup and manual update checks:
  - [ ] Startup check runs after app readiness initialization.
  - [ ] Startup check must not block normal app usage.
  - [ ] Manual check is available from Settings/About update UI.
- [ ] **7.8** Add release artifact naming contract:
  - [ ] `FoundrySetup-x64.msi`.
  - [ ] `FoundrySetup-arm64.msi`.
  - [ ] Velopack release metadata files.
- [ ] **7.9** Use Velopack CLI directly for MSI packaging:

```powershell
vpk pack --msi --instLocation PerMachine --packId Foundry --packVersion <YY.M.D-build.Build> --packDir <publish-dir> --mainExe Foundry.exe
```

- [ ] **7.10** Do not add or maintain a dedicated WiX project for Foundry.
- [ ] **7.11** Preserve `Foundry.Connect` and `Foundry.Deploy` ZIP release assets.
- [ ] **7.11.1** Replace old main `Foundry-x64.exe` and `Foundry-arm64.exe` release assets with Velopack package/MSI artifacts.
- [ ] **7.12** Commit:

```powershell
git commit -m "feat: add velopack distribution flow"
```

**Validation**

- [ ] **7.13** Publish WinUI app for `win-x64`.
- [ ] **7.14** Run `vpk pack --msi --instLocation PerMachine` for `win-x64`.
- [ ] **7.15** Install locally.
- [ ] **7.16** Launch installed app.
- [ ] **7.17** Confirm first-run path works.
- [ ] **7.18** Confirm manual update check handles no-update state.
- [ ] **7.19** Repeat for `win-arm64` on ARM64 runner or machine.

## Phase 8: GitHub Actions And Release Workflow Migration

**Priority:** high.

**Goal:** make CI and release automation match the new project structure and packaging model.

- [ ] **8.1** Update `.github\workflows\ci.yml`.
- [ ] **8.2** Restore `src\Foundry.slnx`.
- [ ] **8.3** Build matrix:
  - [ ] `x64` on `windows-latest`.
  - [ ] `ARM64` on `windows-11-arm`.
- [ ] **8.4** Confirm WinUI build works on both runners.
- [ ] **8.5** Confirm WPF `Foundry.Connect` and `Foundry.Deploy` still build.
- [ ] **8.6** Confirm tests run:
  - [ ] `Foundry.Core.Tests`.
  - [ ] `Foundry.App.Tests` only if it exists.
  - [ ] `Foundry.Connect.Tests` when unchanged or when impacted by shared changes.
  - [ ] `Foundry.Deploy.Tests` when unchanged or when impacted by shared changes.
- [ ] **8.7** Update `.github\workflows\release.yml`.
- [ ] **8.8** Keep manual dispatch during migration.
- [ ] **8.9** Keep scheduled release disabled until final cutover.
- [ ] **8.10** Replace old single-file `Foundry-x64.exe` and `Foundry-arm64.exe` release output with Velopack package output.
- [ ] **8.11** Continue publishing `Foundry.Connect` ZIPs.
- [ ] **8.12** Continue publishing `Foundry.Deploy` ZIPs.
- [ ] **8.13** Ensure workflow installs required tools:
  - [ ] `.NET 10 SDK`.
  - [ ] Velopack CLI.
- [ ] **8.14** Do not add direct WiX build steps unless Velopack documentation later requires them explicitly.
- [ ] **8.15** Keep date-based release versioning.
- [ ] **8.16** Keep GitHub release tags in the existing visible format:
  - [ ] Tag format: `vYY.M.D.Build`.
  - [ ] Example: `v26.4.30.1`.
- [ ] **8.17** Map GitHub release tag to Velopack package version:
  - [ ] `vYY.M.D.Build` maps to `YY.M.D-build.Build`.
  - [ ] Example: `v26.4.30.1` maps to `26.4.30-build.1`.
  - [ ] Example: `v26.4.30.2` maps to `26.4.30-build.2`.
- [ ] **8.17.1** Apply version values consistently:
  - [ ] `Version`: `YY.M.D.Build`.
  - [ ] `AssemblyVersion`: `YY.M.D.Build`.
  - [ ] `FileVersion`: `YY.M.D.Build`.
  - [ ] `InformationalVersion`: `YY.M.D.Build`.
  - [ ] Velopack `--packVersion`: `YY.M.D-build.Build`.
- [ ] **8.17.2** Validate Velopack ordering for same-day builds before finalizing the release workflow:
  - [ ] Confirm `26.4.30-build.2` is treated as newer than `26.4.30-build.1`.
  - [ ] Confirm update detection works for the target stable channel with the `-build.` prerelease-style suffix.
  - [ ] If Velopack rejects this ordering, stop and choose a different date-based SemVer2 mapping before cutover.
- [ ] **8.18** Commit:

```powershell
git commit -m "ci: update workflows for winui packaging"
```

**Validation**

- [ ] **8.19** Run CI on PR to `feat/winui-migration`.
- [ ] **8.20** Run manual release on a test branch or draft release tag.
- [ ] **8.21** Confirm artifacts are uploaded.
- [ ] **8.22** Confirm release notes are usable by Velopack.
- [ ] **8.23** Confirm Velopack update detection handles `YY.M.D-build.Build` ordering correctly.

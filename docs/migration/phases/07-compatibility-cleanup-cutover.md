# Compatibility, Cleanup, And Cutover Phases

## Phase 17: Foundry.Connect And Foundry.Deploy Compatibility Pass

**Priority:** high before cutover.

**Goal:** prove the migrated WinUI app still produces runtime artifacts consumed by WPF Connect/Deploy.

- [ ] **17.1** Build `Foundry.Connect` as WPF.
- [ ] **17.2** Build `Foundry.Deploy` as WPF.
- [ ] **17.3** Generate WinPE media from WinUI `Foundry`.
- [ ] **17.4** Boot test media in a VM.
- [ ] **17.5** Confirm `Foundry.Connect` starts in WinPE.
- [ ] **17.6** Confirm `Foundry.Connect` reads generated network configuration.
- [ ] **17.7** Confirm `Foundry.Deploy` can be launched from the runtime handoff.
- [ ] **17.8** Confirm deployment configuration loads.
- [ ] **17.9** Confirm both runtime agents use the normalized layout:
  - [ ] `Runtime\Foundry.Connect\<rid>`.
  - [ ] `Runtime\Foundry.Deploy\<rid>`.
- [ ] **17.10** Confirm logs are written to expected locations.
- [ ] **17.11** Commit compatibility or targeted shared-logic fixes only if required:

```powershell
git commit -m "fix: preserve winpe runtime compatibility"
```

**Validation**

- [ ] **17.12** VM smoke test completed.
- [ ] **17.13** Physical USB smoke test completed if hardware is available.
- [ ] **17.14** No schema-breaking changes found.

## Phase 18: Cleanup And Dependency Review

**Priority:** medium.

**Goal:** remove prototype leftovers and reduce long-term maintenance risk.

- [ ] **18.1** Remove DevWinUI placeholder strings and metadata.
- [ ] **18.2** Remove unused settings pages or rename them to product-specific pages.
- [ ] **18.3** Remove unused packages.
- [ ] **18.4** Keep DevWinUI packages as the long-term shell baseline unless a specific package becomes unused after Foundry pages are integrated.
- [ ] **18.5** Review trimming settings:
  - [ ] Disable trimming if it breaks reflection-heavy dependencies.
  - [ ] Add annotations only where needed.
- [ ] **18.6** Remove x86 prototype leftovers:
  - [ ] Remove `x86` from `Platforms`.
  - [ ] Remove `win-x86` from `RuntimeIdentifiers`.
  - [ ] Confirm no x86 publish profile, workflow matrix entry, installer, or release artifact remains.
- [ ] **18.7** Review unpackaged app leftovers from the WinUI template:
  - [ ] Remove or ignore `Package.appxmanifest` if it is not used by the Velopack `WindowsPackageType=None` build.
  - [ ] Remove stale packaged-only context menu declarations if Velopack/unpackaged install path does not use them.
  - [ ] Remove or replace `RuntimeHelper.IsPackaged()` branches that no longer apply.
- [ ] **18.8** Remove `nucs.JsonSettings` from the WinUI app after `IAppSettingsService` is in place.
  - [ ] Confirm no runtime dependency still requires it.
  - [ ] Confirm persisted app settings use the internal settings service.
- [ ] **18.8.1** Confirm the WinUI app has no remaining DevWinUI prototype AppData working directory usage.
- [ ] **18.9** Commit:

```powershell
git commit -m "chore: clean up winui migration leftovers"
```

**Validation**

- [ ] **18.10** `dotnet list .\src\Foundry\Foundry.csproj package`.
- [ ] **18.11** Confirm no placeholder DevWinUI URLs remain.
- [ ] **18.12** Confirm no `bin`, `obj`, `.vs`, or `.csproj.user` files are tracked.

## Phase 19: Documentation Update

**Priority:** medium-low.

**Goal:** make repository and user documentation match the new app.

- [ ] **19.1** Update `README.md`.
- [ ] **19.2** Update developer build docs.
- [ ] **19.3** Update release process docs.
- [ ] **19.4** Update installation/update docs.
- [ ] **19.5** Update screenshots only after UI stabilizes.
- [ ] **19.6** Update docs site if needed:
  - [ ] Foundry standard workflow.
  - [ ] Media creation.
  - [ ] Expert mode.
  - [ ] Configuration localization.
- [ ] **19.7** Commit:

```powershell
git commit -m "docs: document winui foundry migration"
```

**Validation**

- [ ] **19.8** Build docs site if changed.
- [ ] **19.9** Confirm install/update instructions match Velopack artifacts.

## Phase 20: Final Cutover To Main

**Priority:** final.

**Goal:** merge the migration safely and restore production automation.

- [ ] **20.1** Ensure all PRs are merged into `feat/winui-migration`.
- [ ] **20.2** Run full CI on `feat/winui-migration`.
- [ ] **20.3** Run manual release dry run or draft release.
- [ ] **20.4** Verify release artifacts:
  - [ ] Foundry Velopack packages.
  - [ ] Foundry MSI installers.
  - [ ] Foundry.Connect ZIPs.
  - [ ] Foundry.Deploy ZIPs.
- [ ] **20.5** Re-enable Sunday scheduled release only after a successful manual release.
- [ ] **20.6** Merge `feat/winui-migration` into `main`.
- [ ] **20.7** Tag first WinUI release.
  - [ ] Use date-based tag format `vYY.M.D.Build`.
- [ ] **20.8** Monitor first release installation/update telemetry manually through GitHub issues/downloads/log reports.
- [ ] **20.9** Commit release workflow restoration:

```powershell
git commit -m "ci: restore scheduled releases after winui migration"
```

**Validation**

- [ ] **20.10** CI passes on `main`.
- [ ] **20.11** Release workflow succeeds on manual dispatch.
- [ ] **20.12** Scheduled release is enabled again.

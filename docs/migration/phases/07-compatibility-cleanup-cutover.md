# Compatibility, Cleanup, And Cutover Phases

## Phase 17: Foundry.Connect And Foundry.Deploy Compatibility Pass

**Priority:** high before cutover.

**Goal:** prove the migrated WinUI app still produces runtime artifacts consumed by WPF Connect/Deploy.

**Prerequisites and boundary:** Run Phase 17 after Phase 16.E final media command enablement is complete. Phase 17 should prove compatibility; only commit targeted fixes when the smoke tests expose a concrete incompatibility.

- [ ] **17.1** Build `Foundry.Connect` as WPF.
- [ ] **17.2** Build `Foundry.Deploy` as WPF.
- [ ] **17.3** Generate WinPE media from WinUI `Foundry`.
  - [ ] ISO media.
  - [ ] USB media on a disposable drive when hardware is available.
  - [ ] Standard workflow without optional Network/Autopilot settings.
  - [ ] Expert workflow with deploy localization and generated Connect configuration.
  - [ ] Expert workflow with Autopilot enabled when a test profile is available.
- [ ] **17.4** Boot test media in a VM.
- [ ] **17.5** Confirm `Foundry.Connect` starts in WinPE.
- [ ] **17.6** Confirm `Foundry.Connect` reads generated network configuration.
- [ ] **17.7** Confirm `Foundry.Deploy` can be launched from the runtime handoff.
- [ ] **17.8** Confirm deployment configuration loads.
- [ ] **17.8.1** Confirm `Foundry.Deploy` reads generated Autopilot configuration when Autopilot is enabled and a profile is embedded.
- [ ] **17.8.2** Confirm `Foundry.Deploy` keeps Autopilot operator controls visible when Autopilot is enabled but no profile is embedded.
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
  - [ ] Complete effective Deploy config remains accepted by `Foundry.Deploy`.
  - [ ] Complete effective Connect config remains accepted by `Foundry.Connect`.
  - [ ] Generated media does not rely on sparse missing-root defaults.

## Phase 18: Cleanup And Dependency Review

**Priority:** medium.

**Goal:** remove prototype leftovers and reduce long-term maintenance risk.

**Boundary:** Phase 18 removes only confirmed obsolete migration/prototype leftovers. Do not remove the archived WPF reference before the first stable WinUI release has been validated, because it remains the behavior reference for compatibility investigations. Do not remove DevWinUI shell assets, `AppData.json` navigation metadata, or DevWinUI packages that remain part of the long-term shell baseline.

- [ ] **18.1** Remove DevWinUI placeholder strings and metadata.
- [ ] **18.2** Remove unused settings pages or rename them to product-specific pages.
- [ ] **18.3** Remove unused packages.
- [ ] **18.4** Keep DevWinUI packages as the long-term shell baseline unless a specific package becomes unused after Foundry pages are integrated.
- [ ] **18.5** Review trimming settings:
  - [ ] Disable trimming if it breaks reflection-heavy dependencies.
  - [ ] Add annotations only where needed.
- [ ] **18.6** Remove x86 prototype leftovers:
  - [ ] Remove `x86` from WinUI app `Platforms` only if it still exists.
  - [ ] Remove `win-x86` from WinUI app `RuntimeIdentifiers` only if it still exists.
  - [ ] Confirm no x86 publish profile, workflow matrix entry, installer, or release artifact remains.
  - [ ] Do not remove legitimate `x86` references for Windows Kits paths, ADK tooling, driver metadata, third-party asset documentation, or WPF runtime support.
- [ ] **18.7** Review unpackaged app leftovers from the WinUI template:
  - [ ] Keep `Package.appxmanifest` only if the WinUI build or Visual Studio tooling still requires it; otherwise remove it.
  - [ ] Remove stale packaged-only context menu declarations only if Velopack/unpackaged install path does not use them.
  - [ ] Remove or replace `RuntimeHelper.IsPackaged()` branches only when they are unreachable or contradict the selected unpackaged Velopack model.
  - [ ] Document any retained unpackaged-template artifact with the concrete reason it is still required.
- [ ] **18.8** Confirm Phase 11 removed `nucs.JsonSettings` from the WinUI app.
  - [ ] Confirm no runtime dependency still requires it.
  - [ ] Confirm persisted app settings use the internal settings service.
- [ ] **18.8.1** Confirm Phase 11 removed or replaced remaining DevWinUI prototype AppData working directory usage.
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

**Boundary:** Repository docs live in this repo. User-facing documentation site updates may need the adjacent `foundry-osd.github.io` repository and should be handled as a separate docs-site change when release links, screenshots, or workflow pages need to change. Do not update user-facing workflow screenshots before Phase 16.E and Phase 17 smoke validation are complete.

- [ ] **19.1** Update `README.md`.
- [ ] **19.2** Update developer build docs.
- [ ] **19.3** Update release process docs.
- [ ] **19.4** Update installation/update docs.
- [ ] **19.5** Update screenshots only after UI stabilizes.
- [ ] **19.6** Update docs site if needed:
  - [ ] Confirm the exact adjacent repository path before editing.
  - [ ] Download links point to `FoundrySetup-x64.msi` and `FoundrySetup-arm64.msi`.
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
- [ ] **20.5** Verify installed app startup on a clean machine or clean VM:
  - [ ] Windows App SDK runtime/bootstrap initialization succeeds before WinUI APIs are used.
  - [ ] Missing or present Windows App SDK runtime state is handled by the selected Velopack MSI distribution model.
  - [ ] Installed app launches without requiring Visual Studio or a development environment.
- [ ] **20.6** Commit release workflow restoration after a successful manual release dry run:

```powershell
git commit -m "ci: restore scheduled releases after winui migration"
```

- [ ] **20.7** Merge `feat/winui-migration` into `main`.
- [ ] **20.8** Tag first WinUI release.
  - [ ] Use date-based tag format `vYY.M.D.Build`.
- [ ] **20.9** Re-enable Sunday scheduled release only after the first manual WinUI release succeeds from `main`.
- [ ] **20.10** Monitor first release installation/update telemetry manually through GitHub issues/downloads/log reports.
- [ ] **20.11** Keep the WPF reference archive until the first stable WinUI release has been validated.
- [ ] **20.12** After merge validation, delete merged feature branches and clean up migration worktrees.

**Validation**

- [ ] **20.13** CI passes on `main`.
- [ ] **20.14** Release workflow succeeds on manual dispatch.
- [ ] **20.15** Scheduled release is enabled again.

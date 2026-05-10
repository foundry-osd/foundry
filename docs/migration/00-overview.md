# Foundry WPF to WinUI 3 Migration Plan

> **Scope:** migrate the main `Foundry` application from WPF to WinUI 3 while keeping `Foundry.Connect` and `Foundry.Deploy` as WPF projects. Targeted changes in `Foundry.Connect` or `Foundry.Deploy` are allowed when they simplify shared contracts or improve maintainability without changing their UI technology.
>
> **Reference branch:** `main`.
>
> **Main repository:** `E:\Github\Foundry Project\foundry`.
>
> **Current WPF app:** `E:\Github\Foundry Project\foundry\src\Foundry`.
>
> **Current WinUI 3 prototype:** `F:\Foundry`.
>
> **Planning date:** 2026-04-30.
>
> **Step IDs:** actionable migration checkboxes use `phase.step` identifiers, for example `1.1`, `2.3`, or `7.4`.

## Goals

- [ ] Replace the WPF `Foundry` app with a WinUI 3 app.
- [ ] Keep `Foundry.Connect` and `Foundry.Deploy` as WPF apps, while allowing targeted shared-logic changes when they improve architecture or simplify contracts.
- [ ] Extract reusable business logic into `Foundry.Core`.
- [ ] Treat `Foundry.Core` as the Windows business core for Foundry, not as a cross-platform pure domain library.
- [ ] Preserve behavior and external contracts by default, but do not require a line-for-line logic migration.
- [ ] Keep the old WPF `Foundry` app available as a read-only reference during the migration, then remove the archive after the first stable WinUI release validation.
- [ ] Preserve release-critical contracts for WinPE media creation, runtime handoff, configuration files, Connect/Deploy GitHub release assets, and local debug overrides.
- [ ] Replace the current GitHub-release-only update check with a Velopack-based installer and update flow for the main `Foundry` app.
- [ ] Keep the migration incremental, reviewable, and recoverable.

## Migration Baseline Summary

This section records the baseline captured before the WinUI migration phases were executed. Use the phase records and cutover checklist for the current implementation state.

- [ ] `E:\Github\Foundry Project` is a workspace folder, not a Git repository.
- [ ] The app repository is `E:\Github\Foundry Project\foundry`.
- [ ] The current solution is `src\Foundry.slnx`.
- [ ] Current projects:
  - [ ] `src\Foundry` WPF app to migrate.
  - [ ] `src\Foundry.Connect` WPF app to keep.
  - [ ] `src\Foundry.Deploy` WPF app to keep.
  - [ ] `src\Foundry.Tests`, current WPF-era tests to retire and selectively rewrite.
  - [ ] `src\Foundry.Connect.Tests`, unchanged unless a shared change requires updates.
  - [ ] `src\Foundry.Deploy.Tests`, unchanged unless a shared change requires updates.
- [ ] Current build uses `.NET 10`, `net10.0-windows`, nullable enabled, WPF enabled globally through `src\Directory.Build.props`.
- [ ] Current release workflow runs every Sunday at `03:00 UTC` and publishes portable binaries/ZIPs.
- [ ] Current `Foundry` update logic only checks GitHub releases; it does not install updates.
- [ ] The WinUI 3 prototype at `F:\Foundry` is a shell app with DevWinUI navigation/settings scaffolding, but no migrated business logic.
- [ ] The WinUI 3 prototype contains local `bin`, `obj`, and `.vs` folders that must not be moved into the repository.
- [ ] The UI migration is not a 1:1 WPF screen rewrite; DevWinUI is the long-term baseline for shell/navigation chrome, and Foundry workflow pages should use native WinUI or Windows Community Toolkit controls when they provide a clearer product-specific page experience.
- [ ] The business logic migration is not a 1:1 code rewrite; targeted refactoring is allowed when it improves readability, maintainability, or shared boundaries.

## External Guidance Captured

- [ ] WinUI 3 is provided through Windows App SDK.
- [ ] For unpackaged or externally packaged Windows App SDK apps, startup must handle Windows App SDK runtime initialization correctly before using Windows App SDK APIs.
- [ ] Validate the selected unpackaged Velopack MSI distribution model on a clean machine or clean VM, including Windows App SDK runtime/bootstrap initialization before WinUI APIs are used.
- [ ] `DeploymentManager` APIs can help identify or initialize Windows App SDK runtime package state where applicable, but the migration must not rely on packaged-app-only assumptions for the unpackaged Velopack MSI path.
- [ ] Velopack supports Windows installer/update packaging and can generate an `.msi` installer with `vpk pack --msi`.
- [ ] The target installer mode is Velopack MSI with `--instLocation PerMachine`, not a repository-owned WiX installer project.
- [ ] The target app packaging model is unpackaged WinUI with `WindowsPackageType=None`.
- [ ] Velopack update integration requires app startup code such as `VelopackApp.Build().Run()` and an `UpdateManager` flow for checking/downloading/applying updates.
- [ ] WinUI localization should be planned around Windows App SDK resource APIs such as `.resw`, `ResourceLoader`, `ApplicationLanguages.PrimaryLanguageOverride`, and `ms-resource` lookup, not direct WPF resource binding.
- [ ] DevWinUI navigation localization uses `AppData.json` metadata; the schema exposes `LocalizeId` and `UsexUid`, so Foundry navigation localization must respect that mechanism before adding any custom fallback.
- [ ] Velopack `--packVersion` must be SemVer2-compatible; Foundry keeps date-based `vYY.M.D.Build` release tags and maps them to `YY.M.D-build.Build` for Velopack packages.

## Non-Negotiable Migration Constraints

- [ ] Do not migrate `Foundry.Connect` to WinUI 3.
- [ ] Do not migrate `Foundry.Deploy` to WinUI 3.
- [ ] Do not treat `Foundry.Connect` and `Foundry.Deploy` as untouchable if a targeted non-UI change improves shared logic or reduces duplication.
- [ ] Do not recreate the WPF UI layout 1:1.
- [ ] Keep DevWinUI as the long-term shell baseline for navigation, title bar, and shell behavior, but do not keep DevWinUI page-level controls when native WinUI or Windows Community Toolkit equivalents are simpler, better supported, or visually more coherent.
- [ ] Do not require an application restart when changing the Foundry UI language.
- [ ] Keep WinUI `Foundry` app data under `C:\ProgramData\Foundry`; do not use AppData working directories inherited from the DevWinUI prototype.
- [ ] Do not add UI tests for WPF views, WinUI pages, XAML, bindings, visual layout, or framework behavior.
- [ ] Rebuild the Foundry test architecture cleanly instead of preserving the old `Foundry.Tests` project as-is.
- [ ] Do not break existing WinPE runtime paths:
  - [ ] `X:\Foundry\Config`.
  - [ ] `X:\Foundry\Runtime`.
  - [ ] `X:\Foundry\Seed`.
  - [ ] `startnet.cmd` bootstrap behavior.
- [ ] Do not break `Foundry.Connect` and `Foundry.Deploy` release asset names consumed by bootstrap/update logic.
- [ ] Replace old main `Foundry-x64.exe` and `Foundry-arm64.exe` release assets with Velopack package/MSI artifacts.
- [ ] Do not silently change configuration JSON schemas.
- [ ] Generate complete effective `Foundry.Connect` and `Foundry.Deploy` runtime configuration files, not sparse patch-style files.
- [ ] Do not store WinPE runtime secrets in plaintext when they must be embedded for unattended execution.
- [ ] Do not use DPAPI for generated WinPE runtime secrets because the authoring Windows context is not available inside WinPE.
- [ ] Do not preserve poor internal structure only for 1:1 migration fidelity when a small, well-tested refactor reduces future risk.
- [ ] Do not move UI-specific WPF types into `Foundry.Core`.
- [ ] Do not introduce new dependencies unless they remove a real migration risk.
- [ ] Keep commits small and Conventional Commit compliant.
- [ ] Prefer scoped Conventional Commits when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, `refactor(logging): ...`, or `docs(migration): ...`.

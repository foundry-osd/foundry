# Release And Installation

This page records the current Foundry OSD release and installation contract after the WinUI migration.

## Release Automation

- [ ] The release workflow runs from `.github\workflows\release.yml`.
- [ ] Automatic releases are scheduled every Tuesday at `13:00 UTC`, which targets `15:00` in France during daylight saving time.
- [ ] Manual releases use `workflow_dispatch`.
- [ ] Manual releases can set `force` when a release is required without `src` changes.
- [ ] Scheduled releases are skipped when no files under `src\` changed since the latest published release.
- [ ] Release tags keep the public format `vYY.M.D.Build`.
- [ ] Velopack package versions map tags to `YY.M.D-build.Build`.

## Installer Assets

- [ ] The desktop app display name is `Foundry OSD`.
- [ ] The package ID, executable name, and GitHub release tag prefix remain `Foundry`.
- [ ] Foundry OSD MSI assets are:
  - [ ] `FoundrySetup-x64.msi`.
  - [ ] `FoundrySetup-arm64.msi`.
- [ ] `Foundry.Connect` and `Foundry.Deploy` remain runtime agents and keep their ZIP release assets:
  - [ ] `Foundry.Connect-win-x64.zip`.
  - [ ] `Foundry.Connect-win-arm64.zip`.
  - [ ] `Foundry.Deploy-win-x64.zip`.
  - [ ] `Foundry.Deploy-win-arm64.zip`.

## Velopack Packaging

- [ ] Foundry OSD is packaged with Velopack MSI generation.
- [ ] MSI installation scope is `PerMachine`.
- [ ] MSI bootstrap dependencies include:
  - [ ] .NET Desktop Runtime.
  - [ ] Microsoft Edge WebView2 Runtime.
  - [ ] Microsoft Visual C++ 14.4 runtime.
- [ ] MSI custom welcome, readme, conclusion, banner, logo, and license options are intentionally disabled until Velopack MSI customization issues are resolved.
- [ ] Installed update checks use Velopack and GitHub Releases.
- [ ] Source runs do not exercise the installed Velopack update path.

## Validation

- [ ] Clean-machine validation must confirm MSI dependency bootstrap, app startup, manual update checks, and no-update state.
- [ ] The next scheduled release from `main` must be monitored to confirm the `src`-change gate and published asset set.

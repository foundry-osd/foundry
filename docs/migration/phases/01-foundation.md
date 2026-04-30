# Foundation Phases

## Phase 1: Migration Freeze And Safety Setup

**Priority:** critical.

**Goal:** prevent automatic releases and establish a stable migration lane before moving projects.

- [x] **1.1** Create branch `feat/winui-migration` from `main`.
- [x] **1.2** Disable the Sunday automatic release in `.github\workflows\release.yml`.
- [x] **1.3** Keep `workflow_dispatch` release capability available for manual emergency releases.
- [x] **1.4** Add a clear workflow comment explaining the schedule is disabled during the WinUI migration.
- [x] **1.5** Keep CI on `main` and PRs to `main`.
- [x] **1.6** Run the full CI matrix for PRs targeting `feat/winui-migration`.
- [x] **1.7** Record the last known good release tag before migration starts: `v26.4.26.1`.
- [x] **1.8** Record current release asset names and consumers:
  - [x] Main app release assets: `Foundry-x64.exe`, `Foundry-arm64.exe`.
  - [x] Runtime release assets: `Foundry.Connect-win-x64.zip`, `Foundry.Connect-win-arm64.zip`, `Foundry.Deploy-win-x64.zip`, `Foundry.Deploy-win-arm64.zip`.
  - [x] Current consumers: README download badges consume the main app assets; WinPE bootstrap/local embedding consumes Connect and Deploy zip assets.
- [x] **1.9** Confirm branch protection state on `main`: branch protection is currently not configured.
- [x] **1.10** Confirm `feat/winui-migration` is not used for production releases: release jobs are restricted to `refs/heads/main`.
- [x] **1.11** Create a GitHub milestone or issue set for the migration phases: issue `#107`.
- [x] **1.12** Commit:

```powershell
git commit -m "chore: pause scheduled releases during winui migration"
```

**Validation**

- [x] **1.13** `git diff -- .github\workflows\release.yml`.
- [x] **1.14** Confirm no scheduled release can run automatically.
- [x] **1.15** Confirm manual release dispatch still exists.

## Phase 2: Repository Shape And Project Boundary Decision

**Priority:** critical.

**Goal:** define where each project lives before code migration starts.

- [x] **2.1** Choose final solution layout:

```text
src/
  Foundry/                  # WinUI 3 app, final main app name
  Foundry.Core/             # UI-neutral Windows business logic
  Foundry.Connect/          # Existing WPF app, targeted non-UI changes allowed
  Foundry.Deploy/           # Existing WPF app, targeted non-UI changes allowed
  Foundry.Core.Tests/       # Clean Windows business logic test project
  Foundry.App.Tests/        # Optional non-UI app orchestration tests only if needed
  Foundry.Connect.Tests/    # Existing tests
  Foundry.Deploy.Tests/     # Existing tests
archive/
  Foundry.WpfReference/     # Temporary old WPF app reference, not built or restored by tooling
```

- [x] **2.2** Put the archive under `archive\Foundry.WpfReference`.
- [x] **2.3** Keep `archive\Foundry.WpfReference` outside the build graph and remove it after final WinUI cutover plus first stable WinUI release validation.
  - Note: the archived project file uses the `.csproj.reference` extension so repository-wide dependency submission does not restore obsolete reference code.
- [x] **2.4** Delete the old `src\Foundry.Tests` project during the WinUI shell import; rewrite only valuable business coverage in clean test projects later.
- [x] **2.5** Record that a clean `src\Foundry.Core.Tests` project will be created for business logic in Phase 4.
- [x] **2.6** Record that `src\Foundry.App.Tests` will be created only if non-UI app orchestration tests are needed.
- [x] **2.7** Do not create tests for WinUI pages, XAML, bindings, or visual behavior.
- [x] **2.8** Keep `Foundry.Connect.Tests` and `Foundry.Deploy.Tests` unchanged unless a `Foundry` or `Foundry.Core` shared change flows into those projects.
- [x] **2.9** Update `.gitignore` before importing WinUI prototype:
  - [x] Ignore `.vs\`.
  - [x] Ignore `bin\`.
  - [x] Ignore `obj\`.
  - [x] Ignore `*.csproj.user`.
- [x] **2.10** Confirm `Directory.Build.props` can support both WPF and WinUI:
  - [x] Move `UseWPF` out of the global property group.
  - [x] Keep WPF enabled in WPF application projects while the current WPF app remains in place.
  - [x] Keep nullable and version metadata centralized.
  - [x] Add project-specific WinUI settings only to `src\Foundry\Foundry.csproj` after Phase 3 imports the WinUI app.
- [x] **2.11** Commit:

```powershell
git commit -m "chore: define winui migration project layout"
```

**Validation**

- [x] **2.12** `dotnet restore .\src\Foundry.slnx --nologo`.
- [x] **2.13** `dotnet build .\src\Foundry.slnx -c Release -p:Platform=x64 --no-restore --nologo`.

## Phase 3: Archive WPF Foundry And Import WinUI Prototype

**Priority:** critical.

**Goal:** move the old WPF app out of the build graph and bring the WinUI app into the repository cleanly.

- [x] **3.1** Move current `src\Foundry` to `archive\Foundry.WpfReference`.
- [x] **3.2** Preserve its folder structure for reference.
- [x] **3.3** Remove the archived WPF project from `src\Foundry.slnx`.
- [x] **3.4** Copy the WinUI prototype from `F:\Foundry` into `src\Foundry`.
- [x] **3.5** Exclude these prototype files/folders:
  - [x] `F:\Foundry\.vs`.
  - [x] `F:\Foundry\bin`.
  - [x] `F:\Foundry\obj`.
  - [x] `F:\Foundry\Foundry.csproj.user`.
- [x] **3.6** Review `src\Foundry\Foundry.csproj` after import:
  - [x] Keep `UseWinUI`.
  - [x] Set `WindowsPackageType` to `None`.
  - [x] Keep `WindowsAppSDKSelfContained` decision explicit.
  - [x] Remove `x86` and `win-x86`; supported Foundry architectures are `x64` and `ARM64`.
  - [x] Remove placeholder or unused dependencies.
  - [x] Keep Velopack only if Phase 7 will use it.
- [x] **3.7** Fix namespace mismatches introduced by the prototype:
  - [x] `MainWindow.xaml` currently declares `x:Class="Foundry.Views.MainWindow"`.
  - [x] Confirm final namespace convention for app shell.
- [x] **3.8** Update `src\Foundry.slnx`:
  - [x] Add the imported WinUI `src\Foundry\Foundry.csproj`.
  - [x] Keep `Foundry.Connect`.
  - [x] Keep `Foundry.Deploy`.
  - [x] Keep `Foundry.Connect.Tests`.
  - [x] Keep `Foundry.Deploy.Tests`.
  - [x] Remove the old `Foundry.Tests` project from the solution and repository.
  - [x] Add `Foundry.Core` and `Foundry.Core.Tests` only after Phase 4 creates them.
- [x] **3.9** Commit:

```powershell
git commit -m "chore: import winui foundry shell"
```

**Validation**

- [x] **3.10** `dotnet restore .\src\Foundry.slnx --nologo`.
- [x] **3.11** `dotnet build .\src\Foundry.slnx -c Debug -p:Platform=x64 --no-restore --nologo`.
- [x] **3.12** Run the WinUI app locally.
- [x] **3.13** Confirm `Foundry.Connect` still builds.
- [x] **3.14** Confirm `Foundry.Deploy` still builds.

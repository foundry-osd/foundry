# Contributing to Foundry

Thank you for helping improve Foundry. Contributions must be focused, testable, and safe for a Windows deployment project that performs privileged and destructive operations.

## Before you start

- Search existing [issues](https://github.com/foundry-osd/foundry/issues) and pull requests.
- Open an issue before substantial behavior, architecture, or schema changes.
- Documentation problems and requests may be reported here. Submit user-documentation changes to the [GitBook repository](https://github.com/foundry-osd/GitBook) and follow its contribution guide.
- Never include credentials, tokens, passwords, certificates, tenant data, hardware hashes, device identifiers, or unsanitized logs and screenshots.

All contributions are submitted under the repository's [MIT License](LICENSE). Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Development environment

Foundry development requires Windows, Git, PowerShell, and the .NET 10 SDK. Visual Studio with the .NET desktop and Windows application development tooling is recommended for WinUI 3 and WPF work. Windows ADK and its matching Windows PE add-on are required for media creation and WinPE integration testing, but not for every library-only change.

Restore the repository from PowerShell:

```powershell
dotnet tool restore
dotnet restore .\src\Foundry.slnx --nologo
```

## Solution map

| Project | Responsibility |
| --- | --- |
| `Foundry` | WinUI 3 desktop authoring application |
| `Foundry.Core` | Shared business logic, configuration, validation, media creation, and orchestration |
| `Foundry.WinPE` | Single WinPE executable: guardian (system preparation, the deployment key relay between Foundry Connect and Foundry Deploy, startup diagnostics) that also hosts the Connect and Deploy modes and the native deployment worker |
| `Foundry.Connect` | WPF network-provisioning library hosted by Foundry.WinPE |
| `Foundry.Deploy` | WPF deployment library hosted by Foundry.WinPE |
| `Foundry.AutopilotInteractive` | WPF interactive Autopilot registration runtime that Deploy installs into the target OS and PostInstall runs during specialize |
| `Foundry.PostInstall` | .NET console runtime for built-in and custom post-installation actions, restart/resume, and cleanup before OOBE |
| `Foundry.Localization` | Shared cultures and resource-based localization |
| `Foundry.Telemetry` | Shared telemetry contracts, privacy rules, and implementations |
| `Foundry.Utilities` | Independent reusable technical mechanisms |
| `Foundry.Wpf` | Shared WPF building blocks for the WPF runtimes: styles, controls, dialogs, motion, theme, and WPF localization services |

Keep WinUI 3 concerns in `Foundry`, shared WPF building blocks in `Foundry.Wpf`, and application-specific WPF concerns in the WPF runtime that owns them. `Foundry.Core` must not depend on UI projects. `Foundry.Utilities` is a leaf project and must not reference another Foundry project. Runtime-specific workflows stay in the runtime that owns them.

## Make a change

- Create a focused branch and keep unrelated changes out of the pull request.
- Follow existing architecture, localization, logging, and nullable-reference patterns.
- Foundry.WinPE guardian and PostInstall console messages use English. Keep Foundry OSD authoring controls translated in every supported culture.
- Put reusable business rules in the appropriate non-UI project and add the smallest valuable tests.
- Do not add automated tests for views, bindings, code-behind, or framework behavior unless the change specifically warrants it.
- Update repository documentation when behavior, packaging, install paths, release assets, or user-facing workflows change.
- Use English Conventional Commit titles, for example `fix(deploy): handle missing driver package`.

The Foundry OSD authoring configuration and the single Foundry.WinPE runtime configuration (`foundry.winpe.config.json`, with shared root sections plus `connect` and `deploy` sections) are separate compatibility contracts, next to the separately versioned PostInstall contracts. The file sits next to a single provisioning marker, `foundry.winpe.provisioning-source.txt`; Connect requires the file under WinPE or when `FOUNDRY_WINPE_CONFIG` is set, while Deploy starts with defaults when it is absent outside WinPE. Bump only a contract whose persisted or generated behavior changes. Use the latest published release as the production baseline and update the matching generator, runtime, and compatibility tests together.

Foundry.PostInstall execution plans, checkpoints, and runtime manifests also have versioned contracts. Keep Core, Deploy, and PostInstall compatible when changing these contracts; preserve the binding between a checkpoint and its exact execution plan.

## Validate the change

Run formatting first:

```powershell
.\scripts\Test-FoundryFormat.ps1
```

If it fails, run `.\scripts\Format-Foundry.ps1`, review the changes, and rerun the check.

Build and test x64 changes locally:

```powershell
dotnet restore .\src\Foundry.slnx --nologo
dotnet build .\src\Foundry.slnx -c Release -p:Platform=x64 -p:ContinuousIntegrationBuild=true -warnaserror --no-restore --nologo

$testProjects = Get-ChildItem .\src -Directory -Filter *.Tests |
    ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" }

foreach ($testProject in $testProjects) {
    dotnet run --project $testProject -c Release -p:Platform=x64 --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Validate ARM64 when the change affects runtime behavior, packaging, architecture-specific code, or deployment assets. CI runs formatting, Release builds, and all test projects for both x64 and ARM64.

CI treats build warnings as errors. Existing targeted suppressions remain in effect; fix new warnings instead of adding blanket suppressions.

Foundry OSD publishes without trimming or Native AOT, matching its Velopack packaging settings, and keeps ReadyToRun enabled in Release. Foundry.AutopilotInteractive retains its self-contained WPF publication without trimming or Native AOT. Foundry.WinPE (which hosts Foundry.Connect and Foundry.Deploy) is published through `scripts/Publish-FoundryWinPE.ps1` as a self-contained single-file `Foundry.WinPE.exe` without trimming, for x64 and ARM64, and shipped as `Foundry.WinPE-<rid>.zip`. WPF's native libraries stay beside the executable and the bundle is not compressed, so nothing is extracted at startup and boot.wim compression is not doubled. The release workflow also publishes `Foundry.Connect-<rid>.zip` and `Foundry.Deploy-<rid>.zip` from the same publish, with the executable renamed `Foundry.Connect.exe` / `Foundry.Deploy.exe`: they exist only for boot media created by Foundry OSD v26.10.18.1 or earlier, which download them by name from the latest release. Under those file names `Foundry.WinPE.exe` shows a blocking "This boot media is obsolete" screen (localized, readiness reported to the legacy launcher, exit code 20 when closed) and never deploys. Old media that cannot reach GitHub (offline, or GitHub API rate limit) still start their embedded Deploy. Keep these asset names unchanged.

Foundry.PostInstall is also published as a self-contained single-file x64 and ARM64 executable through `scripts/Publish-FoundryPostInstall.ps1`, with no extraction at startup (no self-extracting content or native libraries, no compression, no symbols). The archive `Foundry.PostInstall-<rid>.zip` holds `Foundry.PostInstall.exe`, `Launch.cmd` and the runtime manifest (`foundry.postinstall.json`) at its root; its name is unchanged. Foundry OSD copies these three files into boot.wim at `X:\Foundry\PostInstall` on every media, whether or not Domain Join is enabled, and verifies the manifest hashes. PostInstall follows the WinPE debug selection. The manifest `releaseTag` is informational; Deploy checks the execution contract, target architecture and file hashes before disk preparation, and a missing or damaged `X:\Foundry\PostInstall` fails preflight with a localized error before the disk is touched. Verify both architectures when changing this publication contract.

Runtimes are delivered from the release of the program that requests them, never from a newer release:

- Foundry OSD copies the Foundry.WinPE files of its own release (the single-file executable, WPF's native libraries and the startup manifests) directly into boot.wim at `X:\Foundry\WinPE`, with no archive in the image; `Launch.cmd` there starts `Foundry.WinPE.exe`, which hosts Connect and Deploy. It copies PostInstall to `X:\Foundry\PostInstall` the same way, on every media, including **Update USB**. Foundry.WinPE, Connect, Deploy and PostInstall are never downloaded, repaired or unpacked at boot, and the guardian resolves nothing. The USB build and **Update USB** delete `<cache>\Runtime`, which only older media used.
- Deploy stages PostInstall from `X:\Foundry\PostInstall`. Only Autopilot Interactive is still downloaded by Deploy on release media, from the release matching its own version, verified against its SHA256 digest and installed directly into the target OS.
- `FOUNDRY_RELEASE_TAG` selects the release for Deploy's Autopilot Interactive download; `FOUNDRY_DEPLOY_RELEASE_TAG` overrides it for Deploy.
- The newer-release notice reads the redirect of `https://github.com/foundry-osd/foundry/releases/latest`, not the GitHub REST API, so devices behind one public address do not share the unauthenticated API limit. Only Deploy shows it, once the network is ready; Connect never looks up the latest release online.
- Unpublished local builds (version `1.0.0.0`) resolve to the latest published release and log a warning. Published versions never use the latest release. Debug media use their local payloads (`FOUNDRY_WINPE_DEBUG_WINPE`, `FOUNDRY_WINPE_DEBUG_WINPE_PROJECT`, `FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE`; PostInstall and Autopilot Interactive follow the WinPE debug flag; `scripts/Enable-LocalWinPeDeploy.ps1 -WinPeArchivePath <Foundry.WinPE-<rid>.zip>` sets them) and skip the release tag check. A locally built PostInstall is copied into boot.wim like the release one. `FOUNDRY_WINPE_CONFIG` (full path) overrides the runtime configuration read by the guardian, Connect and Deploy.

Foundry.AutopilotInteractive is published as self-contained x64 and ARM64 archives through `scripts/Publish-FoundryAutopilotInteractive.ps1`, but production media never carry it. In interactive Autopilot mode, Foundry Deploy downloads `Foundry.AutopilotInteractive-<rid>.zip` from the GitHub release matching its own version (`v<Deploy version>`, never `latest`), verifies its SHA256 digest, and installs it directly into the target OS, never into WinPE storage or a cache. Only debug media provision it: `scripts/Enable-LocalWinPeDeploy.ps1` sets `FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT`, Foundry OSD writes the debug archive into boot.wim at `X:\Foundry\AutopilotInteractive\Foundry.AutopilotInteractive.zip`, and Deploy on debug media reads it there (and refuses to download a published build). Release media never carry or read it.

`Foundry.PostInstall.Tests` covers runtime actions, process supervision, checkpoints, restart/resume, and cleanup. Real Windows Setup/OOBE, native servicing, vendor hardware, and ARM64 execution require separate acceptance checks; unit tests do not replace them.

Use disposable virtual machines, test disks, non-production tenants, and non-production credentials for manual media and deployment testing. Foundry workflows can erase disks and exercise privileged network or cloud operations.

## Open a pull request

- Use an English Conventional Commit title.
- Explain the reason, main changes, and validation performed.
- Link issues with `Closes #123` when appropriate.
- Include screenshots or recordings for visible UI changes.
- Call out breaking changes, schema changes, incomplete validation, and platform limitations.

Maintainers may request changes to keep project boundaries, deployment safety, release compatibility, or documentation accurate.

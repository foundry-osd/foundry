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
| `Foundry.Bootstrap` | .NET console runtime for Windows PE boot orchestration, runtime payload preparation, the deployment key relay between Foundry Connect and Foundry Deploy, and startup diagnostics |
| `Foundry.Connect` | WPF network-provisioning runtime included in boot media |
| `Foundry.Deploy` | WPF deployment runtime included in boot media |
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
- Bootstrap and PostInstall console messages use English. Keep Foundry OSD authoring controls translated in every supported culture.
- Put reusable business rules in the appropriate non-UI project and add the smallest valuable tests.
- Do not add automated tests for views, bindings, code-behind, or framework behavior unless the change specifically warrants it.
- Update repository documentation when behavior, packaging, install paths, release assets, or user-facing workflows change.
- Use English Conventional Commit titles, for example `fix(deploy): handle missing driver package`.

Configuration files used by Foundry OSD, Foundry Connect, and Foundry Deploy are separate compatibility contracts. Bump only a contract whose persisted or generated behavior changes. Use the latest published release as the production baseline and update the matching generator, runtime, and compatibility tests together.

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

Foundry OSD publishes without trimming or Native AOT, matching its Velopack packaging settings, and keeps ReadyToRun enabled in Release. Foundry.Connect, Foundry.Deploy, and Foundry.AutopilotInteractive retain their self-contained WPF publication without trimming or Native AOT. Foundry.Bootstrap is published as a self-contained console application using the same single-file release settings for x64 and ARM64.

Foundry.PostInstall is also published as self-contained single-file x64 and ARM64 archives through `scripts/Publish-FoundryPostInstall.ps1`. Bootstrap resolves it independently using the same media payload, verification, and repair rules as Connect and Deploy, then passes the prepared runtime to Deploy for target staging. PostInstall follows Deploy's debug selection and media storage conventions. Its archive includes its own runtime manifest (`foundry.postinstall.json`) with the `releaseTag` it was published from; Deploy checks the execution contract, release tag, and target architecture before disk preparation. Verify both architectures when changing this publication contract.

Runtimes are delivered from the release of the program that requests them, never from a newer release:

- Foundry OSD writes the Bootstrap and Connect archives of its own release to boot media, plus PostInstall when Domain Join is enabled, including **Update USB**. Release media do not carry Deploy or Autopilot Interactive.
- Bootstrap downloads Deploy, and PostInstall when the media does not carry it, from its own release at each start; USB media keep that copy on the cache volume and reuse it while it belongs to the media release.
- Bootstrap starts the authenticated payloads stored on the media. When one is missing or fails authentication, it downloads the same asset from its own release, accepts it only when it matches the media trust manifest or the release asset digest, shows the repair warning, and does not write the repaired copy back to the media. Connect is resolved before network setup and keeps a 5-second lookup deadline.
- Deploy downloads Autopilot Interactive and, when recovery is needed, PostInstall from its own release. It uses a staged PostInstall runtime only when the manifest `releaseTag` matches its own release; a missing or different tag triggers recovery before the target disk is erased.
- `FOUNDRY_RELEASE_TAG` selects the release for every runtime; `FOUNDRY_CONNECT_RELEASE_TAG`, `FOUNDRY_DEPLOY_RELEASE_TAG`, and `FOUNDRY_POSTINSTALL_RELEASE_TAG` override it per runtime.
- Unpublished local builds (version `1.0.0.0`) resolve to the latest published release and log a warning. Published versions never use the latest release. Debug media use their local payloads and skip the release tag check.

Foundry.AutopilotInteractive is published as self-contained x64 and ARM64 archives through `scripts/Publish-FoundryAutopilotInteractive.ps1`, but production media never carry it. In interactive Autopilot mode, Foundry Deploy downloads `Foundry.AutopilotInteractive-<rid>.zip` from the GitHub release matching its own version (`v<Deploy version>`, never `latest`), verifies its SHA256 digest, and installs it directly into the target OS, never into WinPE storage or a cache. Only debug media provision it: `scripts/Enable-LocalWinPeDeploy.ps1` sets `FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT`, Bootstrap forwards the media archive to Deploy, and Deploy on debug media refuses to download a published build.

`Foundry.PostInstall.Tests` covers runtime actions, process supervision, checkpoints, restart/resume, and cleanup. Real Windows Setup/OOBE, native servicing, vendor hardware, and ARM64 execution require separate acceptance checks; unit tests do not replace them.

Use disposable virtual machines, test disks, non-production tenants, and non-production credentials for manual media and deployment testing. Foundry workflows can erase disks and exercise privileged network or cloud operations.

## Open a pull request

- Use an English Conventional Commit title.
- Explain the reason, main changes, and validation performed.
- Link issues with `Closes #123` when appropriate.
- Include screenshots or recordings for visible UI changes.
- Call out breaking changes, schema changes, incomplete validation, and platform limitations.

Maintainers may request changes to keep project boundaries, deployment safety, release compatibility, or documentation accurate.

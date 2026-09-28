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
| `Foundry.Bootstrap` | .NET console runtime for Windows PE boot orchestration, runtime payload preparation, and startup diagnostics |
| `Foundry.Connect` | WPF network-provisioning runtime included in boot media |
| `Foundry.Deploy` | WPF deployment runtime included in boot media |
| `Foundry.PostInstall` | .NET console runtime for built-in and custom post-installation actions, restart/resume, and cleanup before OOBE |
| `Foundry.Localization` | Shared cultures and resource-based localization |
| `Foundry.Telemetry` | Shared telemetry contracts, privacy rules, and implementations |
| `Foundry.Utilities` | Independent reusable technical mechanisms |

Keep WinUI 3 concerns in `Foundry` and WPF concerns in their owning runtime. `Foundry.Core` must not depend on UI projects. `Foundry.Utilities` is a leaf project and must not reference another Foundry project. Runtime-specific workflows stay in the runtime that owns them.

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

Foundry OSD publishes without trimming or Native AOT, matching its Velopack packaging settings, and keeps ReadyToRun enabled in Release. Foundry.Connect and Foundry.Deploy retain their self-contained WPF publication without trimming or Native AOT. Foundry.Bootstrap is published as a self-contained console application using the same single-file release settings for x64 and ARM64.

Foundry.PostInstall is also published as self-contained single-file x64 and ARM64 archives through `scripts/Publish-FoundryPostInstall.ps1`. Bootstrap resolves it independently using the same release lookup, archive verification, and cache rules as Connect and Deploy, then passes the prepared runtime to Deploy for target staging. PostInstall follows Deploy's debug selection and media storage conventions. Its archive includes its own runtime manifest; Deploy checks the execution contract and target architecture before disk preparation. Verify both architectures when changing this publication contract.

`Foundry.PostInstall.Tests` covers runtime actions, process supervision, checkpoints, restart/resume, and cleanup. Real Windows Setup/OOBE, native servicing, vendor hardware, and ARM64 execution require separate acceptance checks; unit tests do not replace them.

Use disposable virtual machines, test disks, non-production tenants, and non-production credentials for manual media and deployment testing. Foundry workflows can erase disks and exercise privileged network or cloud operations.

## Open a pull request

- Use an English Conventional Commit title.
- Explain the reason, main changes, and validation performed.
- Link issues with `Closes #123` when appropriate.
- Include screenshots or recordings for visible UI changes.
- Call out breaking changes, schema changes, incomplete validation, and platform limitations.

Maintainers may request changes to keep project boundaries, deployment safety, release compatibility, or documentation accurate.

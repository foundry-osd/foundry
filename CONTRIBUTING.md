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
| `Foundry.Connect` | WPF network-provisioning runtime included in boot media |
| `Foundry.Deploy` | WPF deployment runtime included in boot media |
| `Foundry.Localization` | Shared cultures and resource-based localization |
| `Foundry.Telemetry` | Shared telemetry contracts, privacy rules, and implementations |
| `Foundry.Utilities` | Independent reusable technical mechanisms |

Keep WinUI 3 concerns in `Foundry` and WPF concerns in their owning runtime. `Foundry.Core` must not depend on UI projects. `Foundry.Utilities` is a leaf project and must not reference another Foundry project. Runtime-specific workflows stay in the runtime that owns them.

## Make a change

- Create a focused branch and keep unrelated changes out of the pull request.
- Follow existing architecture, localization, logging, and nullable-reference patterns.
- Put reusable business rules in the appropriate non-UI project and add the smallest valuable tests.
- Do not add automated tests for views, bindings, code-behind, or framework behavior unless the change specifically warrants it.
- Update repository documentation when behavior, packaging, install paths, release assets, or user-facing workflows change.
- Use English Conventional Commit titles, for example `fix(deploy): handle missing driver package`.

Configuration files used by Foundry OSD, Foundry Connect, and Foundry Deploy are separate compatibility contracts. Bump only a contract whose persisted or generated behavior changes. Use the latest published release as the production baseline and update the matching generator, runtime, and compatibility tests together.

## Validate the change

Run formatting first:

```powershell
.\scripts\Test-FoundryFormat.ps1
```

If it fails, run `.\scripts\Format-Foundry.ps1`, review the changes, and rerun the check.

Build and test x64 changes locally:

```powershell
dotnet restore .\src\Foundry.slnx --nologo
dotnet build .\src\Foundry.slnx -c Release -p:Platform=x64 -p:ContinuousIntegrationBuild=true --no-restore --nologo

$testProjects = Get-ChildItem .\src -Directory -Filter *.Tests |
    ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" }

foreach ($testProject in $testProjects) {
    dotnet run --project $testProject -c Release -p:Platform=x64 --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Run local validation on an available native architecture. Without an ARM64 host, record ARM64 qualification as unavailable; an x64 cross-build does not establish native behavior. CI runs formatting, Release builds, and all test projects on native x64 and ARM64 runners.

Use disposable virtual machines, test disks, non-production tenants, and non-production credentials for manual media and deployment testing. Foundry workflows can erase disks and exercise privileged network or cloud operations.

## Open a pull request

- Use an English Conventional Commit title.
- Explain the reason, main changes, and validation performed.
- Link issues with `Closes #123` when appropriate.
- Include screenshots or recordings for visible UI changes.
- Call out breaking changes, schema changes, incomplete validation, and platform limitations.

Maintainers may request changes to keep project boundaries, deployment safety, release compatibility, or documentation accurate.

## Bundled tool verification and updates

Run the read-only source check from the repository root:

```powershell
.\scripts\Test-FoundryBundledTools.ps1
```

The checker validates fixed bundled binary identities, ServiceUI's Microsoft Authenticode signature, and required license/provenance/notice files. It never executes, downloads, installs or repairs a bundled program. `SourceIdentity=verified` describes the checked repository bytes; `SourcePackageVerification=unverified` and `RedistributionVerification=unverified` preserve the outstanding ServiceUI evidence limits.

Optionally supply an already-produced Deploy publish folder or an already-accessible WinPE filesystem root:

```powershell
.\scripts\Test-FoundryBundledTools.ps1 -DeployPublishRoot 'C:\OwnedArtifacts\Deploy'
.\scripts\Test-FoundryBundledTools.ps1 -WinPeRoot 'C:\OwnedArtifacts\WinPE' -Architecture x64
```

These arguments only read the supplied local files. They do not mount an image. Deploy scope checks the external notice/provenance files; ServiceUI inside a single-file executable is not inspected by that scope. WinPE scope checks the selected 7-Zip executable and license/readme. Omitted artifact scopes are reported as not supplied. The checker is not a full release-package validator or native launch test.

Before adopting different bytes:

1. Obtain the fixed-version official distribution and record its final source URL, package version and SHA-256. Do not substitute a third-party rehost as authenticated provenance.
2. Extract to an owned temporary directory without running the installer. Inspect PE architecture, file version, hashes and applicable Authenticode publisher/chain; compare the extracted file to the proposed repository bytes.
3. Review the actual distribution's redistribution terms and accompanying notices. Signer identity alone establishes no redistribution rights. If the historical source cannot be reconstructed, keep its verification status explicitly unverified.
4. Update the provenance record, checker pins and notices together, preserving existing 7-Zip license/readme files. Run the checker against source and relevant produced artifacts.
5. Perform separately authorized native launch qualification on the intended architecture before claiming support. Reading ARM64 PE headers or cross-building on x64 is not native ARM64 qualification.

ServiceUI metadata currently identifies only x64. Do not replace it or assume ARM64 emulation support as part of a documentation-only provenance change.

## Release verification

The Release workflow computes one commit and version, runs reusable CI against that identity, then builds each architecture on its native runner. `Set-FoundryBuildVersion.ps1` applies the four build version properties consistently. Build-generated version changes are not source release-version commits.

Connect and Deploy archives run `--validate-package --expected-version <version> --expected-runtime win-x64` before normal application startup. The workflow supplies a fresh `DOTNET_BUNDLE_EXTRACT_BASE_DIR`. This checks exact resources, architecture, native DLL loadability and Deploy's embedded ServiceUI and external notices. It does not create views, initialize deployment/network services, run ServiceUI, install an MSI or prove WinPE bootability.

Each native job produces a receipt containing the commit, versions and measured asset hashes. `Test-FoundryReleaseAssets.ps1` requires both architecture receipts, installers, runtime archives and complete current-version full/delta feed references before merging artifacts. Conflicting duplicate names fail verification. Receipts describe the trusted build job; they are not signatures.

Only the complete verified inventory can reach `Publish-FoundryRelease.ps1`. Its Stage mode creates or resumes one matching draft, refuses public or immutable collisions, and verifies uploaded names, sizes and SHA-256 values. Publish mode rechecks the exact release ID, tag, commit, draft state and complete asset set before publication. The default workflow input keeps the result as a draft. An upload or verification failure leaves publication blocked; rerun the failed jobs to resume the same prepared identity. Do not publish an incomplete draft manually or use Velopack's implicit upload command to bypass this gate.

Keep manual installer, UI, WinPE, physical-media and hardware qualification separate from these automated package checks.

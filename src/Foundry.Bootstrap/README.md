# Foundry.Bootstrap

The .NET 10 console runtime prepares a WinPE session, waits for Foundry Connect, resolves the deployment payload, and launches Foundry Deploy. Boot media still invokes the existing PowerShell bootstrap during this first migration step. Media provisioning and release workflow integration follow separately.

## Development

Run the executable tests from the repository root:

```powershell
dotnet run --project src/Foundry.Bootstrap.Tests/Foundry.Bootstrap.Tests.csproj -c Release -p:Platform=x64
```

The executable accepts `--help` and `--version` outside WinPE. Normal execution requires WinPE and uses the existing `X:\Foundry` layout, including generated configuration, provisioned runtimes, the Deploy seed archive, and architecture-specific 7-Zip under `Tools\7zip`.

To qualify a self-contained payload with the current Connect/Deploy publication settings:

```powershell
dotnet publish src/Foundry.Bootstrap/Foundry.Bootstrap.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:GenerateDocumentationFile=false
```

Use `win-arm64` and `Platform=ARM64` for ARM64. Automated build and publication checks do not establish that the executable works in a particular WinPE image.

## Behavior and diagnostics

- A ready **Foundry Cache** volume takes precedence over `X:\Foundry\Runtime`.
- Connect first uses provisioned content. Debug-provisioned content bypasses normal release lookup. A successful Connect run precedes clock/timezone preparation and any USB Connect refresh.
- Existing `FOUNDRY_CONNECT_*`, `FOUNDRY_DEPLOY_*`, and `FOUNDRY_RELEASE_TAG` archive/tag overrides retain their precedence. A supplied archive checksum must match before staging is promoted.
- Downloads use bounded, streamed HTTP requests. Transient retry starts the transfer again; partial curl downloads from the PowerShell implementation are not resumed.
- Connect exit `20` means operator cancellation. Other nonzero exits fail boot. Neither path launches Deploy.
- Cancelling a wait never kills Connect or Deploy. A successful result means Deploy was launched; readiness acknowledgement belongs to a later migration step.
- Serilog writes `FoundryBootstrap.log` with the shared session and structured log format. Cache copies are best effort. No Bootstrap-specific PostHog instrumentation is included in this step.

## WinPE qualification before cutover

Follow the [manual testing procedure](MANUAL-TESTING.md) for both a quick runtime check and a cold boot using a copied image.

Validate the published executable on representative x64 and ARM64 images with the existing provisioned assets. Cover USB and ISO boot, debug overrides, unavailable Internet with cached content, checksum and extraction failures, Connect cancellation, and missing application binaries. Check compressed single-file extraction space and startup time, readable progress in a narrow console, and the log/session evidence after each failure. Keep this qualification separate from unit test and desktop publish results.

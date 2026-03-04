# Findings & Decisions

## Requirements
- Add a GitHub release workflow triggered by `release.published`.
- Build `Foundry` and `Foundry.Deploy` for `win-x64` and `win-arm64`.
- Upload `Foundry` as direct `.exe` assets with the version in the filename.
- Upload `Foundry.Deploy` as one `.zip` per runtime.
- Update the WinPE bootstrap to download only the matching runtime zip.
- Add runtime-specific cache validation and refresh using a `manifest` file.
- Remove `PublishTrimmed=false` from publish flows.
- Write code in English, simplify logic, and remove obsolete code.

## Research Findings
- The repo currently has no release workflow, only label/stale automation workflows.
- `src/Directory.Build.props` centrally controls assembly version metadata.
- `FoundryBootstrap.ps1` already detects runtime architecture and downloads one deploy zip, but uses a shared cache path and generic asset fallback.
- The current bootstrap preserves the downloaded zip after extraction.
- `Foundry` and `Foundry.Deploy` both rely on bundled `Assets\\7z` files via `AppContext.BaseDirectory`, so `IncludeAllContentForSelfExtract=true` is justified.
- Local PowerShell does not provide `ConvertFrom-Yaml`, and the local Python environment does not include PyYAML, so workflow validation had to rely on manual inspection plus build verification.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Use exact runtime-specific deploy asset names | Removes ambiguous fallback logic and keeps bootstrap deterministic |
| Use a per-runtime cache directory | Prevents x64/arm64 collisions in cached deploy content |
| Keep only extracted files plus `manifest` after a successful refresh | Matches the chosen cache strategy and still preserves refresh metadata |
| Version only the `Foundry` executable asset names | Matches the requested release contract |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Local YAML parsing helpers were unavailable | Verified the workflow manually and validated the rest of the implementation with script parsing and `dotnet build` |

## Resources
- `src/Directory.Build.props`
- `scripts/Publish-FoundryDeploy.ps1`
- `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`
- `src/Foundry/Services/WinPe/MediaOutputService.cs`
- GitHub Actions docs via Context7

## Visual/Browser Findings
- GitHub Actions official docs confirm `release` events and `release.published` are supported workflow triggers.
- Official guidance supports uploading assets to an existing release via CLI or release actions.

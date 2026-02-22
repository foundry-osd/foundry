# Findings & Decisions

## Requirements
- Use bundled 7-Zip sources from `src/Foundry/Assets/7z` (no runtime download in bootstrap).
- Ensure extraction operations in `Foundry` and `Foundry.Deploy` use 7z with no fallback.
- Update provisioning so WinPE image contains required 7z files.

## Research Findings
- Inventory across `src/Foundry` and `src/Foundry.Deploy` found extraction logic in:
  - `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1` (7z bootstrap tooling still had download/tar extraction path).
  - `src/Foundry/Services/WinPe/WinPeDriverPackageService.cs` (`expand.exe` for CAB + vendor-specific EXE extraction switches).
  - `src/Foundry.Deploy/Services/DriverPacks/DriverPackPreparationService.cs` (`expand.exe` for CAB + `ZipFile.ExtractToDirectory` for ZIP).
- `src/Foundry/Assets/7z` now exists with runtime binaries for `x64` and `arm64`.
- `MediaOutputService` currently provisions local deploy ZIP into mounted WinPE image, but does not yet provision 7z tools.
- `Foundry.csproj` only embeds bootstrap script; 7z assets were not explicitly configured for runtime output.
- `Foundry.Deploy.csproj` had no 7z asset inclusion.
- Remaining extraction paths were migrated:
  - `WinPeDriverPackageService`: now 7z for `.cab`, `.zip`, `.exe` with hard-fail when bundled 7z is missing.
  - `DriverPackPreparationService`: now 7z for `.cab` and `.zip` with hard-fail when bundled 7z is missing.
- WinPE bootstrap no longer downloads 7z and now requires pre-provisioned `7za.exe` under `ProgramData\Foundry\Deploy\Tools\7zip\<runtime>`.
- WinPE image customization now provisions bundled `7za.exe` into mounted image.
- `WinPeToolResolver` no longer validates `expand.exe` and `WinPeToolPaths` no longer carries `ExpandPath`.
- Build outputs for both projects now include:
  - `Assets\7z\x64\7za.exe`
  - `Assets\7z\arm64\7za.exe`

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Use bundled 7z assets in repo as the only bootstrap source | Remove network dependency and keep behavior deterministic |
| Enforce 7z-only extraction in both projects with hard failure if 7z missing | Matches user requirement "aucun fallback" |
| Provision 7z into WinPE image during customization | Ensures bootstrap extraction works offline at boot |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| None yet |  |

## Verification Notes
- `dotnet build src/Foundry/Foundry.csproj -nologo` succeeded (0 warnings, 0 errors).
- `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj -nologo` succeeded (0 warnings, 0 errors).
- Global grep confirmed no remaining extraction usage of `Expand-Archive`, `ZipFile.ExtractToDirectory`, or `expand.exe` in `src/Foundry` and `src/Foundry.Deploy`.

## Resources
- `src/Foundry/Assets/7z`

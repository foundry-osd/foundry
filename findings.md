# Findings & Decisions

## Requirements
- Determine why WinPE bootstrap output is not visible in startup logs.
- Identify configurations (winpeshl.ini, startnet.cmd, bootstrap scripts) tied to visibility.
- Capture process startup settings (UseShellExecute, stdout/stderr redirection) and UI logging path based on repo contents.

## Research Findings
- Ran `rg --files -g 'winpeshl.ini'` in repo root; no matches yet.
- `rg --files -g 'startnet.cmd'` and `rg --files -g '*bootstrap*'` also returned no matches from top-level search.
- `rg -n 'winpeshl'` and `rg -n 'startnet'` found no references anywhere in the repository so far.
- Root contains `src/Foundry` and `src/Foundry.Deploy`, suggesting WinPE assets may live in those subprojects.
- `src/Foundry.Deploy` and `src/Foundry` show the WPF/desktop structure; investigate their Assets/Resources folders for WinPE scripts or boot-time content.
- Foundry WinPE assets sit under `src/Foundry/Assets/WinPe`, which currently contains `FoundryBootstrap.ps1`.
- `FoundryBootstrap.ps1` logs to `X:\Windows\Temp\FoundryBootstrap.log`, relies on BITS + GitHub release metadata, extracts `Foundry.Deploy.exe`, and launches it via `Start-Process` without redirecting stdout/stderr (default `UseShellExecute = $true`).
- `Program.cs` wires WPF services including `IWinPeBuildService`, `IMediaOutputService`, and other WinPE-specific services, implying the GUI orchestrates the bootstrap workflow.
- `.tmp/OSDCloud` contains a copy of the OSDCloud PowerShell module (including a `public-winpe` directory); these files may mirror the WinPE setup that Foundry uses for boot-time scripts.
- `Invoke-OSDCloudPEStartup.ps1` within `.tmp/OSDCloud/public-winpe` uses `Start-Transcript` and `Start-Process` (with `WindowStyle` and `NoExit` flags) to open PE tools, meaning console output is redirected to log files rather than the WinPE shell directly.
- `.sisyphus/drafts/foundry-deploy.md` describes the Foundry.Deploy plan, noting `startnet.cmd` copies `FoundryBootstrap.ps1` into `X:\Windows\System32` and invokes it at boot; it also reinforces logging to `X:\Windows\Temp\FoundryBootstrap.log` and downloading the `Foundry.Deploy` release via BITS.
- Case-insensitive `rg -n` searches for `startnet` and `winpeshl` still return nothing, suggesting the WinPE startup scripts/configs are created as part of the build pipeline rather than checked into source control here.
- `WinPeDefaults.DefaultStartnetPathInImage` is `Windows\\System32\\startnet.cmd`, and `MediaOutputService` rewrites that file when mounting the image—read existing lines (default `wpeinit`) and writes merged lines back—so the actual `startnet.cmd` is constructed during WinPE build rather than stored in source control.
- `MediaOutputService` uses `WinPeDefaults.DefaultBootstrapInvocation` to append `powershell.exe -ExecutionPolicy Bypass -NoProfile -File X:\Windows\System32\FoundryBootstrap.ps1` to `startnet.cmd`, while the default bootstrap script content is stored as the embedded resource `Foundry.WinPe.BootstrapScript` (written into the image before boot), explaining why the startnet entry and script appear in the WinPE image but not the repo.
- `rg -n 'Foundry.WinPe.BootstrapScript'` currently finds nothing in the checked-in files, so the bootstrap script content likely comes from an embedded resource that matches the `FoundryBootstrap.ps1` we already saw in `src/Foundry/Assets/WinPe`.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
|          |           |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Unable to run planning session-catchup.py because WindowsApps python binaries are inaccessible from this environment | Documented here and in task_plan.md; proceed without catchup until a working interpreter is available |

## Resources
- 

## Visual/Browser Findings
- 

---
*Update this file after every 2 view/browser/search operations*
*This prevents visual information from being lost*

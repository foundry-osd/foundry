# Autopilot Hardware Hash Upload - Overview

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md).

## Purpose
This document defines the feasibility, integration approach, and phased implementation plan for adding Windows Autopilot hardware hash capture and upload from WinPE to Foundry.

This feature is intended to complement the existing offline Autopilot JSON profile staging flow. It must not replace the current behavior.


## Branch Strategy
- Foundation branch: `feature/autopilot-hash-upload-foundation`
- Foundation worktree: `C:\Users\mchav\.config\superpowers\worktrees\foundry\autopilot-hash-upload-foundation`
- Phase branches should branch from the foundation branch:
  - `feature/autopilot-hash-upload-config`
  - `feature/autopilot-hash-upload-ui`
  - `feature/autopilot-hash-upload-security`
  - `feature/autopilot-hash-upload-media`
  - `feature/autopilot-hash-upload-runtime`
  - `feature/autopilot-hash-upload-capture`
  - `feature/autopilot-hash-upload-graph`
  - `feature/autopilot-hash-upload-docs`

The foundation branch should remain documentation-first. Implementation branches should be small, reviewable, and merged back into the foundation branch in phase order.


## Pull Request Roadmap
All PR titles must stay in English and use Conventional Commits. Each phase branch should be merged back into the foundation branch before the next phase branch starts.

| Order | Branch | Pull request title | Scope |
| --- | --- | --- | --- |
| 0 | `feature/autopilot-hash-upload-foundation` | `docs(autopilot): plan hardware hash upload from WinPE` | Feasibility, phased integration plan, risk register, test matrix. |
| 1 | `feature/autopilot-hash-upload-config` | `feat(autopilot): add provisioning mode configuration` | Expert and Deploy configuration models, backward compatibility, readiness rules. |
| 2 | `feature/autopilot-hash-upload-ui` | `feat(autopilot): add provisioning method selection` | Autopilot page expanders, mutually exclusive method selection, localized strings. |
| 3 | `feature/autopilot-hash-upload-security` | `feat(autopilot): add secure tenant upload onboarding` | Tenant sign-in, app registration creation, certificate lifecycle, secret handling, permission validation. |
| 4 | `feature/autopilot-hash-upload-media` | `feat(winpe): stage autopilot hash capture assets` | WinPE optional component requirements, x64/ARM64 OA3Tool discovery, media payload layout. |
| 5 | `feature/autopilot-hash-upload-runtime` | `feat(deploy): branch autopilot runtime by provisioning mode` | Deploy startup snapshot, launch validation, runtime state, late deployment step, dry-run manifests. |
| 6 | `feature/autopilot-hash-upload-capture` | `feat(deploy): capture autopilot hardware hash in WinPE` | C# OA3Tool execution service, `PCPKsp.dll` copy, `OA3.xml` parsing, CSV/diagnostic artifacts. |
| 7 | `feature/autopilot-hash-upload-graph` | `feat(autopilot): import hardware hashes with Graph` | C# Graph client, import polling, retry policy, operator-facing errors. |
| 8 | `feature/autopilot-hash-upload-docs` | `docs(autopilot): document WinPE hardware hash upload` | User docs, permissions matrix, troubleshooting, screenshots, release notes. |

Expected PR description structure:
- Summary: one short paragraph.
- Reason: why this phase is needed and what risk it removes.
- Main changes: concise bullet list.
- Testing notes: exact automated commands and manual checks.


## Non-Goals
- Do not remove or redesign the existing offline JSON profile workflow.
- Do not involve Foundry Connect.
- Do not implement hardware hash capture or upload with PowerShell scripts, PowerShell Gallery modules, or community wrapper modules.
- Do not delete existing Intune, Autopilot, or Entra records automatically.
- Do not add group membership automation.
- Do not claim full Microsoft support for WinPE-based hash capture.
- Do not limit the implementation to x64. x64 and ARM64 are both in scope.
- Do not redistribute `PCPKsp.dll` in generated media. Copy it from the applied Windows image during deployment.


## Resolved Decisions
- No capture-only mode in the first implementation. Foundry always captures and uploads, while retaining OA3 and CSV diagnostics for troubleshooting.
- `PCPKsp.dll` copy/load failure is blocking for the Autopilot hash upload workflow because it is a prerequisite for this capture path.
- Duplicate device cleanup is not part of the final implementation. Foundry surfaces duplicate/import errors clearly, retains diagnostics, and continues OS deployment without deleting Intune, Autopilot, or Entra records.


## Source References
- Microsoft Learn: [Manually register devices with Windows Autopilot](https://learn.microsoft.com/en-us/autopilot/add-devices)
- Microsoft Learn: [Microsoft Graph importedWindowsAutopilotDeviceIdentity import](https://learn.microsoft.com/en-us/graph/api/intune-enrollment-importedwindowsautopilotdeviceidentity-import?view=graph-rest-1.0)
- Microsoft Learn: [Using the OA 3.0 tool on the factory floor](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/oa3-using-on-factory-floor?view=windows-11)
- Microsoft Learn: [WinPE optional components reference](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-add-packages--optional-components-reference?view=windows-11)
- Local artifact: `C:\Users\mchav\Downloads\foundry-autopilot-hash-winpe-en.md`
- Local artifact: `C:\Users\mchav\Downloads\HashUpload_WinPE.ps1`


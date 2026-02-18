# Findings & Decisions

## Key Findings
- Existing ADK lifecycle and global progress model can be preserved while implementing WinPE media generation under `Services/WinPe`.
- A dedicated process runner + tool resolver pattern is sufficient to orchestrate ADK tools (`copype`, `MakeWinPEMedia`, `DISM`, `diskpart`, `PowerShell`).
- USB safety must be enforced in service logic (not only UI): USB bus/removable checks + identity checks + double confirmation code.

## Implemented Decisions
| Decision | Rationale |
|----------|-----------|
| Keep `MediaOutputService` as orchestration entry point | Single control plane for ISO/USB workflows |
| Add helper services (`WinPeToolResolver`, `WinPeProcessRunner`, `WinPeMountSession`, `WinPeDriverPackageService`, `WinPeUsbMediaService`) | Clear separation of concerns and better diagnostics |
| Keep structured diagnostics (`WinPeDiagnostic` + `WinPeErrorCodes`) | Better troubleshooting and actionable failures |
| Add USB candidate enumeration API on `IMediaOutputService` | Enables UI-driven safe disk targeting |
| Add xUnit project focused on deterministic validation paths | Avoids requiring ADK during tests while covering safety rules |

## External References Used
- Microsoft Learn: WinPE bootable media creation (`copype`, `MakeWinPEMedia`, `/bootex` usage context).
- Microsoft Support: PCA2023 boot manager remediation guidance and fallback script model.
- Windows PowerShell docs (Storage): disk querying/provisioning command patterns.

## Residual Risks
- Real-world vendor EXE extraction formats vary; fallback attempts are implemented but may need tuning per package generation.
- PCA2023 remediation script parameter contract can vary by script version; current fallback executes script with `-MediaPath`.
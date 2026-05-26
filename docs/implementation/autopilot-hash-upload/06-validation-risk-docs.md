# Autopilot Hardware Hash Upload - Validation Risk And Documentation

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md). Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.

## Cross-Cutting Test Matrix
Automated commands for every implementation PR:

```powershell
dotnet test .\src\Foundry.slnx --configuration Debug /p:Platform=x64
```

CI must pass for:
- x64
- ARM64

ARM64 CI is blocking because hash upload support is in scope for both architectures.

Recommended focused test areas:
- `Foundry.Core.Tests`
  - configuration serialization
  - deploy configuration generation
  - media preflight readiness
  - WinPE asset provisioning
  - OA3 XML and CSV helpers if implemented in Core
- `Foundry.Deploy.Tests`
  - startup snapshot
  - preparation view model
  - launch validation
  - deployment step branching
  - hash capture parser/client abstractions if implemented in Deploy
- `Foundry.Telemetry.Tests`
  - event property policy if new Autopilot mode telemetry properties are added

Manual physical validation matrix:

| Scenario | Required before release |
| --- | --- |
| x64 physical device with Ethernet, user-driven Autopilot | Yes |
| x64 physical device with Wi-Fi, user-driven Autopilot | Yes |
| x64 physical device with TPM 2.0 visible in WinPE | Yes |
| x64 device with existing Autopilot registration | Yes, expected duplicate/error behavior must be clear. |
| ARM64 physical device with Ethernet, user-driven Autopilot | Yes |
| ARM64 physical device with Wi-Fi, user-driven Autopilot | Yes |
| ARM64 physical device with TPM 2.0 visible in WinPE | Yes |
| ARM64 device with existing Autopilot registration | Yes, expected duplicate/error behavior must be clear. |
| JSON profile mode regression on x64 and ARM64 media | Yes |
| Hardware hash upload mode | Yes |
| Self-deploying/pre-provisioning | No, document as not recommended until separately validated. |


## Risk Register
| Risk | Impact | Mitigation |
| --- | --- | --- |
| OA3Tool produces empty or incomplete hash in WinPE | Import fails or device gets unreliable Autopilot behavior | Add `WinPE-SecureStartup`, retain OA3 diagnostics, document fallback to OOBE/full OS. |
| TPM not visible from WinPE | Self-deploying/pre-provisioning unreliable | Do not recommend those scenarios until separately validated. |
| Credentials embedded into media | Tenant compromise if generated media is lost | Encrypt certificate material with the media secret envelope, require explicit confirmation, document generated media as tenant-sensitive, and never write decrypted key material to disk. |
| Media secret key and encrypted secret are both present in the boot image | Encryption can be bypassed by anyone with full media access | Treat envelope encryption as plaintext avoidance/integrity protection, not a hard security boundary. |
| Certificate expires before deployment | Autopilot upload cannot authenticate | Show expired state in Foundry OSD, block hardware hash media generation until certificate regeneration, and let Foundry Deploy continue OS deployment while skipping Autopilot upload. |
| Operator loses one-time PFX/private key material | Existing app certificate cannot be used for new media | Require replacing the active certificate or choosing another valid active certificate by thumbprint, then rebuild boot media. |
| Broad Graph permissions copied from community script | Excessive tenant blast radius | Minimum permission matrix and no destructive final implementation flows. |
| Duplicate devices already exist | Import fails or operator confusion | Surface duplicate/import error clearly, retain sanitized diagnostics, and continue OS deployment without automatic cleanup. |
| Architecture-specific OA3Tool/support file mismatch | Runtime failure | Resolve ADK assets per selected WinPE architecture and validate both x64 and ARM64 media. |
| `PCPKsp.dll` missing from applied OS or copy fails | Autopilot hash upload cannot meet prerequisites | Copy from `<target Windows>\Windows\System32` after OS apply and fail the Autopilot workflow as a blocking prerequisite error if the copy/load operation fails. |
| Retained Autopilot diagnostics accumulate over time | Disk usage or stale troubleshooting data | Retain artifacts by default for debugging, keep the retained set allow-listed and sanitized, and document manual cleanup after diagnostics are no longer needed. |
| UI conflates JSON and hash mode | Invalid media or deployment launch | Explicit `ProvisioningMode` and readiness rules. |


## Documentation Deliverables
- Foundry app documentation:
  - Autopilot provisioning modes.
  - Hardware hash upload setup.
  - Tenant app registration onboarding.
  - Certificate creation, one-time PFX/private key material handling, expiration, repair, and replacement.
  - Group tag default selection.
  - Tenant permissions.
  - Security warning for generated media.
  - Troubleshooting.
- Foundry OSD docs site:
  - New Autopilot hardware hash upload page.
  - Requirements update documenting `WinPE-SecureStartup` as a default WinPE optional component.
  - Product boundaries update explaining the workaround status.
  - Manual test checklist.
- Docusaurus documentation:
  - Use the Docusaurus repository at `E:\Github\Foundry Project\foundry-osd.github.io`.
  - Create a dedicated Docusaurus worktree before editing docs.
  - Create the Docusaurus branch `docs/autopilot-hash-upload` from the current documentation base branch.
  - Use the Docusaurus PR title `docs(autopilot): document WinPE hardware hash upload`.
  - Locate the docs source in that repository before editing by searching for `docusaurus.config.*` or the docs package root.
  - Update pages, sidebars, navigation, screenshots, and release notes when user-facing behavior changes.
  - Run the relevant Docusaurus build or preview command discovered from the docs package scripts if Docusaurus sources are changed.
- Release notes:
  - Mark as x64 and ARM64 with Ethernet and Wi-Fi upload guidance.
  - Mention unsupported or risky self-deploying/pre-provisioning status.

## Phase 8 Release Guardrail Status
Phase 8 adds operator-facing Docusaurus documentation and updates the internal implementation plan. It does not change runtime code.

Documentation validation commands:

```powershell
npm run typecheck
npm run build
```

Release guardrails now documented for operators:
- Hardware hash upload from WinPE is a Foundry-assisted best-effort workflow, not the Microsoft-standard Autopilot registration path.
- Generated media is tenant-sensitive because the boot image contains encrypted certificate material and the media secret key needed to decrypt it in WinPE.
- WinPE Microsoft Graph authentication uses certificate-based app-only authentication only.
- The required Microsoft Graph application permission is `DeviceManagementServiceConfig.ReadWrite.All`.
- `PCPKsp.dll` is copied from the applied Windows image into `X:\Windows\System32` late in deployment before OA3Tool capture.
- Upload failures, expired certificate metadata, Graph errors, duplicate import errors, and Windows Autopilot device visibility timeouts do not fail the Windows deployment.
- Local hash capture prerequisites can block only the Autopilot upload workflow because Foundry cannot produce a valid hash without them.
- Retained diagnostics stay under `<target Windows>\Windows\Temp\Foundry\Logs\AutopilotHash` and must remain sanitized.
- Retained diagnostics are created in a restricted directory where Windows ACL hardening is available.
- Self-deploying mode, pre-provisioning, and unvalidated TPM visibility from WinPE remain unsupported or risky scenarios.
- Destructive duplicate-device cleanup is intentionally out of scope for the final workflow.

Manual validation still required before broad production rollout:
- Connect a clean tenant from Foundry OSD and confirm managed app registration creation, required permission state, and admin consent state.
- Adopt an existing Foundry-managed tenant app registration and confirm certificate discovery, replacement, and retirement behavior.
- Rebuild media after selecting the boot-media PFX and confirm app restart requires selecting the PFX again.
- Follow the published docs in a clean test tenant.
- Validate one clean x64 physical device with Ethernet.
- Validate one clean x64 physical device with Wi-Fi when Wi-Fi media is in scope.
- Validate one clean ARM64 physical device with Ethernet.
- Validate one clean ARM64 physical device with Wi-Fi when ARM64 media is in scope.
- Confirm duplicate-device behavior is clear and does not delete existing Autopilot records.
- Confirm retained diagnostics are present and do not contain tokens, authorization headers, PFX bytes, PFX passwords, decrypted private key material, encrypted secret blobs, media secret keys, or raw Graph payloads.

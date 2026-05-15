# Autopilot Hardware Hash Upload - Validation Risk And Documentation

Part of the [Autopilot hardware hash upload implementation plan](../autopilot-hardware-hash-upload.md).

Implementation agents must follow the repository instructions in [AGENTS.md](../../../AGENTS.md).

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
| Duplicate devices already exist | Import fails or operator confusion | Surface duplicate/import error clearly; defer cleanup automation. |
| Architecture-specific OA3Tool/support file mismatch | Runtime failure | Resolve ADK assets per selected WinPE architecture and validate both x64 and ARM64 media. |
| `PCPKsp.dll` missing from applied OS or copy fails | Autopilot hash upload cannot meet prerequisites | Copy from `<target Windows>\Windows\System32` after OS apply and fail the Autopilot workflow as a blocking prerequisite error if the copy/load operation fails. |
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
  - Update pages, sidebars, navigation, screenshots, and release notes when user-facing behavior changes.
  - Run the relevant Docusaurus build or preview command if Docusaurus sources are changed.
- Release notes:
  - Mark as x64 and ARM64 with Ethernet and Wi-Fi upload guidance.
  - Mention unsupported or risky self-deploying/pre-provisioning status.



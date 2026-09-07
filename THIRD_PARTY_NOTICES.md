# Third-party notices

This repository-level notice covers third-party binary assets committed directly to the Foundry source tree. Managed .NET dependencies and their license links are listed in the application's **About > Licenses** view.

## 7-Zip Extra

Foundry uses parts of the 7-Zip program (`7za.exe`) from the 7-Zip Extra package.

- Upstream: [7-Zip](https://www.7-zip.org/)
- License: GNU LGPL with additional BSD 2-clause and BSD 3-clause notices for portions of `7za.exe`
- Included license files: `src/Foundry.Core/Assets/7z/License.txt` and `src/Foundry.Core/Assets/7z/readme.txt`

This file supplements, and does not replace, the license files distributed with each bundled asset.

## Microsoft ServiceUI

Foundry.Deploy embeds `ServiceUI.exe` to launch the full-Windows Autopilot assistant in the interactive session. The committed binary is x64 (PE machine 8664), version 1.3.0.0, 74,008 bytes, SHA-256 `1BE85A64AAD2C3CAA0DC28705B49A1548E85157F4D2D522C20FEC4B4570A623F`.

- Historical source family: [Microsoft Deployment Toolkit](https://learn.microsoft.com/en-us/intune/configmgr/mdt/).
- Current byte identity and observed Microsoft Authenticode signature are recorded in [ServiceUI.provenance.json](src/Foundry.Deploy/Assets/AutopilotRegistration/ServiceUI.provenance.json), also packaged beside Foundry.Deploy as `ServiceUI.provenance.json`.
- The exact original source package, package hash, and source-file comparison have not been verified. Redistribution terms from that original distribution have not been established in this record. A valid Microsoft signature is not a license determination.
- This is not an ARM64-native binary. Native x64 launch qualification and ARM64 emulation qualification are not established by these metadata checks.

No binary update is implied by this record. See [the maintainer verification procedure](CONTRIBUTING.md#bundled-tool-verification-and-updates) before changing bundled bytes.

## Recorded 7-Zip version

The bundled Extra files and their accompanying readme identify version 26.02. `scripts/Test-FoundryBundledTools.ps1` pins the length, SHA-256, PE architecture and version of all twelve committed 7-Zip EXE/DLL files, plus the license and readme hashes. The files are unsigned; their repository identity does not prove an independent comparison with an upstream archive. Preserve the supplied license information when distributing them.

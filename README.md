<p align="center">
  <img src="Assets/GitHub/readme-logo.png" alt="Foundry logo and project name">
</p>

<p align="center">
  <b>Build bootable Windows deployment media and run guided deployments from WinPE.</b>
</p>

<p align="center">
  <a href="https://github.com/foundry-osd/foundry/releases/latest"><img src="https://img.shields.io/github/v/release/foundry-osd/foundry?display_name=tag&sort=semver&style=flat-square&label=Latest%20Release&color=007ec6" alt="Latest release"></a>
  <a href="https://github.com/foundry-osd/foundry/releases"><img src="https://img.shields.io/github/downloads/foundry-osd/foundry/total?style=flat-square&label=Downloads&color=success" alt="Total downloads"></a>
  <img src="https://img.shields.io/badge/OS%20Scope-Windows%2011%2023H2%20%7C%2024H2%20%7C%2025H2-2563EB?style=flat-square" alt="Windows 11 23H2, 24H2, and 25H2">
  <img src="https://img.shields.io/badge/Architecture-x64%20%7C%20ARM64-2563EB?style=flat-square" alt="Architecture x64 and ARM64">
  <a href="https://github.com/foundry-osd/foundry/blob/main/LICENSE"><img src="https://img.shields.io/github/license/foundry-osd/foundry?style=flat-square&label=License&color=blue" alt="License"></a>
</p>

<p align="center">
  <a href="#download"><strong>Download</strong></a> ·
  <a href="https://docs.foundryosd.com"><strong>Documentation</strong></a> ·
  <a href="#capabilities"><strong>Capabilities</strong></a> ·
  <a href="#requirements"><strong>Requirements</strong></a> ·
  <a href="#workflow"><strong>Workflow</strong></a> ·
  <a href="#screenshots"><strong>Screenshots</strong></a> ·
  <a href="#ecosystem"><strong>Ecosystem</strong></a> ·
  <a href="#support"><strong>Support</strong></a>
</p>

---

## Download

Install the latest MSI that matches the architecture of the admin workstation.

<p align="center">
  <a href="https://github.com/foundry-osd/foundry/releases/latest/download/Foundry-win-x64.msi"><img src="https://img.shields.io/badge/Download-Windows_x64-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Download x64"></a>
  &nbsp;&nbsp;&nbsp;
  <a href="https://github.com/foundry-osd/foundry/releases/latest/download/Foundry-win-arm64.msi"><img src="https://img.shields.io/badge/Download-Windows_ARM64-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Download ARM64"></a>
</p>

Use the MSI release build for normal deployment work. Use [all releases](https://github.com/foundry-osd/foundry/releases) when you need release notes, checksums, or older builds.

Start with the [Quick Start](https://docs.foundryosd.com/start-here/quick-start) guide for the shortest end-to-end path.

## Overview

Foundry Project is a UI-first Windows deployment toolkit for building WinPE-based media, validating network readiness, and running repeatable Windows deployment workflows with deployment-time customization. It separates media authoring, network readiness, and deployment execution into focused application surfaces.

| Surface | Runs where | Purpose |
| --- | --- | --- |
| Foundry OSD | Admin workstation | Check prerequisites, configure deployment behavior, and create ISO or USB media |
| Foundry Connect | WinPE target device | Validate or select network connectivity before deployment continues |
| Foundry Deploy | WinPE target device | Select deployment options and execute the Windows deployment |

This repository contains the Foundry OSD desktop application and the WinPE runtime agents used during deployment.

## Capabilities

| Area | What Foundry provides | Why it matters |
| --- | --- | --- |
| Guided media creation | Build ISO or USB deployment media from a desktop UI | Reduces manual WinPE and scripting work |
| Optional media protection | Require an administrator-defined password before Foundry Deploy initializes | Prevents casual or unauthorized deployment from lost media and protects Deploy credentials and offline Autopilot profiles |
| Ethernet and Wi-Fi | Validate Ethernet, use pre-provisioned Wi-Fi, or connect to supported personal Wi-Fi networks from WinPE | Supports deployments on wired, wireless, enterprise, and non-enterprise networks |
| Enterprise networking | Stage wired 802.1X and enterprise Wi-Fi profiles with optional trusted root CA certificates | Supports corporate network environments |
| Deployment workflow | Choose target disk, Windows image, drivers, firmware, Autopilot, and deployment options from Foundry Deploy | Makes deployment decisions visible before execution |
| Windows customization | Configure machine naming, language, time zone, OOBE, privacy defaults, categorized Windows Optional Features, AppX removals, and AI component controls | Produces cleaner and more predictable Windows installations with fully offline post-apply servicing |
| Autopilot | Stage offline Autopilot JSON profiles or upload hardware hashes from WinPE | Covers offline provisioning and tenant-connected registration |
| Readiness validation | Check ADK readiness, media settings, USB target identity, target disk eligibility, network provisioning, secrets, and Autopilot configuration | Catches missing or risky setup before media creation or deployment |

## Requirements

Prepare an admin workstation with:

- Windows 10 or Windows 11
- Local administrator rights
- Internet access
- Windows ADK `10.1.26100.2454` or later
- Windows PE add-on for the same ADK release

Foundry OSD can help install or upgrade ADK components when they are missing or incompatible.

Current deployment scope:

- Windows 11 `23H2`, `24H2`, and `25H2`
- x64 and ARM64 deployment media
- WinPE boot driver injection for Dell, HP, and custom `.inf` driver folders
- Deployment-time operating system, driver, and firmware choices loaded from the current catalog

## Workflow

Most deployments follow the standard path:

1. Install Foundry OSD on an admin workstation.
2. Create ISO or USB deployment media.
3. Boot the target device from the media.
4. Let the bootstrap open Foundry Connect and validate networking.
5. Let the bootstrap open Foundry Deploy.
6. Select the target disk, operating system, driver pack, and deployment options.
7. Review the summary and start deployment.

Use the dedicated [network](https://docs.foundryosd.com/foundry-osd/network), [Windows Autopilot](https://docs.foundryosd.com/foundry-osd/autopilot), and [customization](https://docs.foundryosd.com/foundry-osd/customization) sections when deployment media must carry predefined connectivity, provisioning, or Windows configuration.

### Protected deployment media

Foundry OSD can optionally require a password before Foundry Deploy initializes. The password is defined by the administrator when creating the media and is never stored in the Foundry configuration or on the media. Deploy credentials and offline Autopilot JSON profiles are encrypted on protected media and decrypted only after successful authorization.

The password cannot be recovered. If it is lost, recreate the deployment media with a new password. Foundry OSD keeps the password only for the current process session, so it must be entered again after restarting Foundry OSD or after disabling and re-enabling password protection.

Foundry Connect continues to handle network secrets automatically before Foundry Deploy starts. Password protection does not replace physical media controls, Secure Boot, restricted boot-device policies, or appropriate tenant permissions; keep deployment media secured and revoke exposed credentials when a device is lost.

## Screenshots

<table>
  <tr>
    <td><img src="Assets/GitHub/Readme/1.png" alt="Foundry screenshot 1"></td>
    <td><img src="Assets/GitHub/Readme/2.png" alt="Foundry screenshot 2"></td>
  </tr>
  <tr>
    <td><img src="Assets/GitHub/Readme/3.png" alt="Foundry screenshot 3"></td>
    <td><img src="Assets/GitHub/Readme/4.png" alt="Foundry screenshot 4"></td>
  </tr>
  <tr>
    <td><img src="Assets/GitHub/Readme/5.png" alt="Foundry screenshot 5"></td>
    <td><img src="Assets/GitHub/Readme/6.png" alt="Foundry screenshot 6"></td>
  </tr>
  <tr>
    <td><img src="Assets/GitHub/Readme/7.png" alt="Foundry screenshot 7"></td>
    <td><img src="Assets/GitHub/Readme/8.png" alt="Foundry screenshot 8"></td>
  </tr>
  <tr>
    <td><img src="Assets/GitHub/Readme/9.png" alt="Foundry screenshot 9"></td>
    <td><img src="Assets/GitHub/Readme/10.png" alt="Foundry screenshot 10"></td>
  </tr>
  <tr>
    <td><img src="Assets/GitHub/Readme/11.png" alt="Foundry screenshot 11"></td>
    <td><img src="Assets/GitHub/Readme/12.png" alt="Foundry screenshot 12"></td>
  </tr>
  <tr>
    <td colspan="2"><img src="Assets/GitHub/Readme/13.png" alt="Foundry screenshot 13"></td>
  </tr>
</table>

## Ecosystem

Foundry Project is split across focused repositories:

- [`foundry`](https://github.com/foundry-osd/foundry): Foundry OSD, Foundry Connect, and Foundry Deploy.
- [`catalog`](https://github.com/foundry-osd/catalog): Catalog automation for operating system, driver pack, and WinPE metadata.
- [`GitBook`](https://docs.foundryosd.com): Documentation, guides, and reference material.

## Telemetry

Foundry OSD collects anonymous usage telemetry to help prioritize project improvements. Telemetry is enabled by default and can be disabled from Settings. Generated Foundry Connect and Foundry Deploy runtime media follow the same setting.

See the [telemetry documentation](https://docs.foundryosd.com/reference/telemetry-and-privacy) for collected events, excluded data, and privacy details.

## Support

Community involvement is welcome.

- **Bugs and feature requests:** Use the [issue tracker](https://github.com/foundry-osd/foundry/issues).
- **Source and local development:** Review the [Foundry repository](https://github.com/foundry-osd/foundry) for solution structure and validation tooling.

## Third-Party Notices

### 7-Zip Extra

This project uses parts of the 7-Zip program (`7za.exe`) from the 7-Zip Extra package.

- Upstream: [https://www.7-zip.org/](https://www.7-zip.org/)
- License: GNU LGPL with additional BSD 2-clause and BSD 3-clause notices for portions of `7za.exe`
- Included license files: `src/Foundry.Core/Assets/7z/License.txt`, `src/Foundry.Core/Assets/7z/readme.txt`

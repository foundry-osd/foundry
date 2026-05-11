<p align="center">
  <img src="Assets/GitHub/readme-logo.png" alt="Foundry logo and project name">
</p>

<p align="center">
  <b>Modern Windows deployment for imaging, provisioning, and repeatable device setup.</b><br>
</p>

<p align="center">
  <a href="https://github.com/foundry-osd/foundry/releases/latest"><img src="https://img.shields.io/github/v/release/foundry-osd/foundry?display_name=tag&sort=semver&style=flat-square&label=Latest%20Release&color=007ec6" alt="Latest release"></a>
  <a href="https://github.com/foundry-osd/foundry/releases"><img src="https://img.shields.io/github/downloads/foundry-osd/foundry/total?style=flat-square&label=Downloads&color=success" alt="Total downloads"></a>
  <img src="https://img.shields.io/badge/Windows-11-2563EB?style=flat-square&logo=windows" alt="Windows 11">
  <img src="https://img.shields.io/badge/OS%20Versions-23H2%20%7C%2024H2%20%7C%2025H2-2563EB?style=flat-square" alt="Supported OS Versions">
  <img src="https://img.shields.io/badge/Architecture-x64%20%7C%20ARM64-2563EB?style=flat-square" alt="Architecture x64 and ARM64">
  <a href="https://github.com/foundry-osd/foundry/blob/main/LICENSE"><img src="https://img.shields.io/github/license/foundry-osd/foundry?style=flat-square&label=License&color=blue" alt="License"></a>
</p>

<p align="center">
  <a href="#-download--installation"><strong>📥 Download</strong></a> ·
  <a href="https://foundry-osd.github.io/"><strong>📖 Documentation</strong></a> ·
  <a href="#-the-foundry-ecosystem"><strong>🌍 Ecosystem</strong></a> ·
  <a href="https://github.com/foundry-osd/foundry/issues"><strong>🐛 Report an Issue</strong></a>
</p>

---


Foundry replaces legacy imaging scripts with Foundry OSD, a clean, fully-guided modern desktop UI. Whether you are deploying dozens of machines in an enterprise or just standardizing your personal setups, Foundry ensures you always have the right drivers, the right OS version, and a repeatable configuration.

<p align="center">
  <img src="Assets/GitHub/social-preview.png" alt="Foundry preview">
</p>

## 📥 Download & Installation

Get started by downloading the latest Foundry OSD MSI installer for your workstation architecture.

<p align="center">
  <a href="https://github.com/foundry-osd/foundry/releases/latest/download/Foundry-win-x64.msi"><img src="https://img.shields.io/badge/Download-Windows_x64-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Download x64"></a>
  &nbsp;&nbsp;&nbsp;
  <a href="https://github.com/foundry-osd/foundry/releases/latest/download/Foundry-win-arm64.msi"><img src="https://img.shields.io/badge/Download-Windows_ARM64-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Download ARM64"></a>
</p>

> 💡 **Next steps:** For prerequisites (like the Windows ADK) and how to configure your first deployment, check out our [Quick Start guide](https://foundry-osd.github.io/docs/start/quick-start).

## ✨ Highlights

*   **Desktop UI First:** Build WinPE-based ISO and bootable USB deployment media straight from a clean Windows application.
*   **Native Windows 11 Support:** Fully supports Windows 11 `23H2`, `24H2`, and `25H2` across both `x64` and `ARM64`.
*   **Automated Driver Matching:** Say goodbye to driver hunting. Enjoy best-in-class automated driver handling for OEMs like Dell, HP, Lenovo, and Microsoft Surface.
*   **Guided Zero-Touch & Lite-Touch:** Interactive prompts for target disk selection, OS version, machine naming, localization, and Autopilot staging natively inside WinPE.

## 🔄 The Deployment Workflow

Foundry breaks down the deployment journey into 4 straightforward steps:

1.  **🏗️ Create Media:** Run the `Foundry OSD` desktop app on your admin PC to craft your deployment media.
2.  **🌐 Connect:** Boot the target device into WinPE. `Foundry Connect` immediately kicks in to validate and secure wired or Wi-Fi network access.
3.  **🎯 Deploy:** `Foundry Deploy` launches a guided UI to select the target disk, desired OS, and auto-fetches the matched hardware drivers.
4.  **✅ Finish:** The OS image is downloaded, applied, and configured. The device reboots into a ready-to-use Windows state.

## 🌍 The Foundry Ecosystem

Foundry is more than just a single executable. It is supported by a modular ecosystem across dedicated repositories ensuring stability and easy contribution:

*   [`foundry`](https://github.com/foundry-osd/foundry) *(This repository)*: The core repository containing the Foundry OSD Windows desktop authoring app and the WinPE runtime agents (`Connect` and `Deploy`).
*   [`catalog`](https://github.com/foundry-osd/catalog): The automated backend engine that dynamically curates driver packs and OS catalogs, ensuring you always inject the exact vendor drivers needed during deployment.
*   [`foundry-osd.github.io`](https://foundry-osd.github.io/): Our comprehensive documentation and developer hub.

## 🛠️ Contributing & Support

We welcome community involvement! 
- **Bugs & Features:** Please report any issues or suggest features on our [Issue Tracker](https://github.com/foundry-osd/foundry/issues).
- **Compile Local:** If you want to contribute code, see the [Developer Setup Guide](https://foundry-osd.github.io/docs/developer) for details on compiling with the `.NET 10 SDK` and `Windows ADK`.

## ⚖️ Third-Party Notices

### 7-Zip Extra
This project uses parts of the 7-Zip program (`7za.exe`) from the 7-Zip Extra package.
- Upstream: [https://www.7-zip.org/](https://www.7-zip.org/)
- License: GNU LGPL with additional BSD 2-clause and BSD 3-clause notices for portions of `7za.exe`
- Included license files: `src/Foundry/Assets/7z/License.txt`, `src/Foundry/Assets/7z/readme.txt`

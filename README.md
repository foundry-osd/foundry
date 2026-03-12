<p align="center">
  <img src="Assets/GitHub/readme-logo.png" alt="Foundry logo" width="220">
</p>

<h1 align="center">Foundry</h1>

<p align="center">
  <a href="https://github.com/mchave3/Foundry/releases/latest"><img src="https://img.shields.io/github/v/release/mchave3/Foundry?display_name=tag&sort=semver&style=flat-square&label=Version&color=2563EB" alt="Latest release"></a>
  <a href="https://github.com/mchave3/Foundry/releases"><img src="https://img.shields.io/github/downloads/mchave3/Foundry/total?style=flat-square&label=Downloads&color=CA8A04" alt="Downloads"></a>
  <a href="https://github.com/mchave3/Foundry/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/mchave3/Foundry/release.yml?branch=main&style=flat-square&label=CI/CD" alt="Foundry release"></a>
  <a href="https://github.com/mchave3/Foundry.Automation/actions/workflows/run-scripts-daily.yml"><img src="https://img.shields.io/github/actions/workflow/status/mchave3/Foundry.Automation/run-scripts-daily.yml?branch=main&style=flat-square&label=Catalog" alt="Foundry.Automation"></a>
  <a href="https://github.com/mchave3/Foundry/blob/main/LICENSE"><img src="https://img.shields.io/github/license/mchave3/Foundry?style=flat-square&label=License&color=16A34A" alt="License"></a>
  <img src="https://img.shields.io/badge/Windows-11-0078D6?style=flat-square" alt="Windows 11">
  <img src="https://img.shields.io/badge/Architecture-x64%20%2F%20ARM64-2563EB?style=flat-square" alt="Architecture x64 / ARM64">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" alt=".NET 10">
</p>

<p align="center">
  Foundry is designed to simplify Windows deployment for end users and IT administrators.
  It helps you create deployment media, boot a device, choose a Windows version, and start a guided deployment experience with a cleaner, more repeatable workflow.
</p>

<p align="center">
  <a href="https://github.com/mchave3/Foundry/releases">Releases</a>
  ·
  <a href="https://github.com/mchave3/Foundry/issues">Issues</a>
  ·
  <a href="https://github.com/mchave3/Foundry/blob/main/LICENSE">License</a>
</p>

![Foundry preview](Assets/GitHub/social-preview.png)

## What Foundry Does

Foundry is built around a simple goal: make Windows deployment easier to prepare, easier to run, and easier to repeat.

Instead of relying on a fully manual installation process, Foundry helps you create ready-to-use deployment media and launch a guided deployment experience once the target device boots from it.

It is intended for people who want a more practical way to deploy Windows with fewer manual steps, less repetition, and a workflow that is easier to standardize across devices.

## Highlights

- Builds WinPE-based ISO and bootable USB deployment media from a desktop UI.
- Simplifies the Windows deployment experience with a guided workflow.
- Lets you choose Windows version, language, edition, and deployment options more easily.
- Supports Windows 11 `23H2` to `25H2` in `x64` and `ARM64`, with 38 languages in the current catalog.
- Retrieves Windows files and drivers from online sources during deployment when needed.
- Supports Dell, HP, Lenovo, and Microsoft Surface driver workflows, with automatic OEM driver pack selection based on detected hardware or driver retrieval through Microsoft Update Catalog.
- Detects and manages Windows ADK and WinPE Add-on requirements.
- Includes logging, cache-aware workflows, CA2023 boot support, and optional driver, firmware, and Autopilot support.

## Deployment Flow

1. Use `Foundry` to generate a deployment ISO or USB drive.
2. Boot the target device from that media.
3. Select the target disk, Windows version, and deployment options.
4. Let Foundry download the Windows files it needs, apply Windows, configure, and finalize the deployment.

## Getting Started

### Use a release

Download the latest packaged build from the [Releases](https://github.com/mchave3/Foundry/releases) page.

Use Foundry to create deployment media, then boot that media on the target device to start the deployment experience.

### Build from source

Requirements:

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows ADK 11 24H2 with the WinPE Add-on for deployment media creation

Build the solution:

```powershell
dotnet build .\src\Foundry.slnx
```

Run the app:

```powershell
dotnet run --project .\src\Foundry\Foundry.csproj
```

### Deployment scope

Foundry currently targets WinPE-based Windows deployment from prepared media.
It is designed to simplify Windows deployment, hardware-aware driver handling, and repeatable deployment workflows for real-world IT usage.

Today, Foundry focuses on Windows 11 deployment for versions `23H2`, `24H2`, and `25H2`, with support for both `x64` and `ARM64`, broad language coverage, automatic OEM driver pack selection for supported hardware, Microsoft Update Catalog driver retrieval, and CA2023-capable boot media.

## Contributing

Foundry is intended to stay approachable for open source contributors. If you want to improve the deployment experience, report a bug, refine the workflow, or propose a new capability, start with an [issue](https://github.com/mchave3/Foundry/issues) or open a pull request directly.

Small, focused contributions are preferred.

## Third-Party Components

### 7-Zip Extra

This project uses parts of the 7-Zip program (`7za.exe`) from the 7-Zip Extra package.

- Upstream: https://www.7-zip.org/
- License: GNU LGPL (with additional BSD 2-clause / BSD 3-clause notices for portions of `7za.exe`)
- Included license files: `src/Foundry/Assets/7z/License.txt`, `src/Foundry/Assets/7z/readme.txt`

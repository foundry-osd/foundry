# Filesystem Layout

## Filesystem Layout Audit Findings

- [ ] Local inspection on 2026-04-30 found `C:\ProgramData\Foundry` contains authoring workspaces and caches:
  - [ ] `Installers`.
  - [ ] `UsbQuery`.
  - [ ] `WinPeWorkspace`.
- [ ] Local inspection found stale `FoundryWinPe_*` workspaces under `C:\ProgramData\Foundry\WinPeWorkspace`, including one failed or retained WinRE extraction workspace with roughly 25 GB of files; this confirms the new app should start with a cleaner layout, not a compatibility layer for the old one.
- [ ] `Installers` contains both `OperatingSystems` and `os`, which suggests naming drift between installer/cache responsibilities.
- [ ] Generated media contracts currently depend on:
  - [ ] `X:\Foundry\Config`.
  - [ ] `X:\Foundry\Runtime`.
  - [ ] `X:\Foundry\Seed`.
  - [ ] `X:\Foundry\Tools\7zip`.
  - [ ] USB cache volume label `Foundry Cache`.
- [ ] `Foundry.Connect` config and network assets are internally consistent:
  - [ ] Config is placed at `X:\Foundry\Config\foundry.connect.config.json`.
  - [ ] Network assets are stored under `X:\Foundry\Config\Network`.
  - [ ] Config-relative paths such as `Network\Wifi\Profiles` resolve correctly.
- [ ] The main migration opportunity is not a broken config path; it is making the filesystem contract explicit, named, testable, and clean from the first WinUI implementation.

## Target Filesystem Layout Reference

### Host Authoring Layout

```text
C:\ProgramData\Foundry\
  Settings\
    appsettings.json
  Cache\
    Installers\
      adksetup.exe
      adkwinpesetup.exe
    OperatingSystems\
      <os-image>.esd
      <os-image>.iso
    Tools\
      curl\
      7zip\
  Workspaces\
    WinPe\
      FoundryWinPe_<arch>_<timestamp>\
        media\
        mount\
        drivers\
        logs\
        temp\
    Iso\
      <ascii-safe-iso-workspace>\
  Temp\
    UsbQuery\
    WinRe\
    Downloads\
  Logs\
    Foundry.log
```

Initial `Settings\appsettings.json` schema:

```json
{
  "schemaVersion": 1,
  "appearance": {
    "theme": "system"
  },
  "localization": {
    "language": "system"
  },
  "updates": {
    "checkOnStartup": true,
    "channel": "stable",
    "feedUrl": null
  },
  "diagnostics": {
    "developerMode": false
  }
}
```

### ISO Media Root

```text
ISO:\
  bootmgr
  bootmgr.efi
  Boot\
  EFI\
  sources\
    boot.wim
```

### WinPE Runtime Layout Inside boot.wim

```text
X:\
  Windows\
    System32\
      startnet.cmd
      FoundryBootstrap.ps1
      curl.exe

  Foundry\
    Config\
      foundry.connect.config.json
      foundry.deploy.config.json
      iana-windows-timezones.json
      Secrets\
        media-secrets.key

      Network\
        Wired\
          Profiles\
          Certificates\
        Wifi\
          Profiles\
          Certificates\

      Autopilot\
        <profile-name>\
          AutopilotConfigurationFile.json

    Runtime\
      Foundry.Connect\
        win-x64\
          Foundry.Connect.exe
        win-arm64\
          Foundry.Connect.exe

      Foundry.Deploy\
        win-x64\
          Foundry.Deploy.exe
        win-arm64\
          Foundry.Deploy.exe

    Seed\
      Foundry.Deploy.zip

    Tools\
      7zip\
        x64\
          7za.exe
        arm64\
          7za.exe
        License.txt
        readme.txt

    Logs\
      FoundryBootstrap.log
      FoundryConnect.log
      FoundryDeploy.log

    Temp\
```

### USB Media Layout

```text
BOOT:\
  bootmgr
  bootmgr.efi
  Boot\
  EFI\
  sources\
    boot.wim
```

```text
Foundry Cache:\
  Runtime\
    Foundry.Connect\
      win-x64\
      win-arm64\
    Foundry.Deploy\
      win-x64\
      win-arm64\

  Cache\
    OperatingSystems\
    DriverPacks\
    Firmware\

  Logs\
  State\
  Temp\
```

### Layout Rules

- [ ] ISO mode is autonomous; required runtime and configuration content lives under `X:\Foundry`.
- [ ] USB mode keeps BOOT minimal and stores persistent runtime/cache data on the `Foundry Cache` partition.
- [ ] Runtime applications always use `Runtime\<ApplicationName>\<rid>`.
- [ ] `Seed\Foundry.Deploy.zip` is a local Deploy seed package, not a legacy host-folder fallback.
- [ ] Large persistent artifacts use `Cache\...`.
- [ ] Disposable working data uses `Temp\...`.
- [ ] Diagnostics use `Logs\...`.
- [ ] App settings use `Settings\appsettings.json`.
- [ ] App settings must not contain secrets or workflow/export configuration.
- [ ] The WinUI app must not use the DevWinUI prototype AppData root as a working directory.
- [ ] The WinUI app must not write app settings, logs, cache, workspaces, or temp files under `%LocalAppData%`.
- [ ] Application code must not fall back to the old host-side folders.

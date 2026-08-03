# PSBootstrapper

`psbootstrapper.exe` is a small native launcher that runs a PowerShell script silently
(no flashing console window). Foundry copies it into `Windows\System32` of every WinPE /
WinRE boot image and invokes it from the boot `Unattend.xml` to launch
`FoundryBootstrap.ps1` hidden.

- Upstream: https://github.com/Grace-Solutions/PowershellBootstrapper
- License: see `LICENSE` in this folder.

## Required binaries

Drop the prebuilt, architecture-matched executables here (these are consumed at build
time and copied to output alongside the 7-Zip assets):

- `x64/psbootstrapper.exe`
- `arm64/psbootstrapper.exe`

The folder layout mirrors `Assets/7z/{x64,arm64}`. These arch subfolders are force-kept
in `.gitignore` (the blanket `x64/` / `arm64/` ignore rules would otherwise exclude them).

## Invocation (as provisioned into the boot image)

```
X:\Windows\System32\psbootstrapper.exe --script-path X:\Windows\System32\FoundryBootstrap.ps1
```

psbootstrapper defaults to `-ExecutionPolicy Bypass -NonInteractive -NoProfile -NoLogo -WindowStyle Hidden`.

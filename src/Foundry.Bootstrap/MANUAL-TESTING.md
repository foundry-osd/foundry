# Testing the standalone Bootstrap runtime

This procedure covers the first Bootstrap migration PR. Foundry OSD still generates media that starts `FoundryBootstrap.ps1`; building OSD from this branch does not automatically select the new executable. Bootstrap-specific PostHog events and application readiness acknowledgements are later work.

Use a test machine or VM and a copy of your Foundry media. A successful Bootstrap run opens Deploy; stop at the wizard rather than starting a Windows deployment. Reboot between scenarios so a previous Connect or Deploy instance cannot confuse the result. USB cache updates persist between boots, so keep a copy if you need an unchanged baseline.

## Obtain the executable

Use the published `Foundry.Bootstrap.exe` matching the target WinPE architecture: `win-x64` or `win-arm64`. The locally prepared test packages are under `artifacts/bootstrap-test` in the implementation worktree. They are development artifacts, not GitHub Release downloads.

To reproduce a publication from the repository root:

```powershell
dotnet publish src/Foundry.Bootstrap/Foundry.Bootstrap.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:GenerateDocumentationFile=false -o artifacts/bootstrap-test/win-x64
```

For ARM64, change the RID, Platform, and output directory. Copy the executable to a separate folder on an accessible USB drive, for example `D:\BootstrapTest\win-x64\Foundry.Bootstrap.exe`. Drive letters in WinPE may differ from the technician workstation; all examples below use `D:` for this test-files drive.

## Quick runtime test with existing media

1. Boot an existing Foundry USB or ISO of the matching architecture with network access disconnected, including any automatically provisioned Wi-Fi. Its generated `X:\Foundry` configuration, runtime content, and bundled tools must remain present; a bare Microsoft WinPE image is insufficient.
2. When the original Foundry Connect appears, **cancel it promptly** and let the PowerShell bootstrap finish. Connect can automatically succeed after its connectivity countdown; do not rely on avoiding a Continue button. Verify that the latest PowerShell bootstrap entry in `X:\Foundry\Logs\FoundryBootstrap.log` reports that Connect was closed by the operator. If the old bootstrap launched Deploy, or you cannot confirm cancellation, reboot and repeat or use the cold-boot image procedure below. Absence of an open Deploy window alone is insufficient because an application could have launched and then crashed.
3. Use the returned command prompt, or open one with Shift+F10 if available. Run these commands in **Command Prompt**, adjusting the source drive:

```bat
mkdir X:\Foundry\Bootstrap
copy /y D:\BootstrapTest\win-x64\Foundry.Bootstrap.exe X:\Foundry\Bootstrap\Foundry.Bootstrap.exe
set FOUNDRY_DIAGNOSTIC_SESSION_ID=BOOT334-01
X:\Foundry\Bootstrap\Foundry.Bootstrap.exe --version
X:\Foundry\Bootstrap\Foundry.Bootstrap.exe
echo BootstrapExitCode=%ERRORLEVEL%
```

Run the exit-code command immediately after Bootstrap returns. Invoke the executable directly, without `start`, so the prompt waits for it. The successful exit code is `0`; cancellation and failure return `1` and are distinguished in the console and log.

For the normal-startup scenario, restore networking after the new Bootstrap has launched Connect. Leave it disconnected for the no-network scenario.

This tests the new runtime against real provisioned assets. It does **not** qualify its first-run service initialization: the original bootstrap has already prepared network services. Complete the cold-boot test below before cutover.

## Scenarios and expected results

| Scenario | Action | Expected result |
| --- | --- | --- |
| Normal startup | Complete Connect with working network access. | Five readable stages; Connect completes before system preparation; Deploy launches once; exit `0`. Confirm the wizard appears, then stop there. |
| Operator cancellation | Cancel the Connect launched by the new Bootstrap. | `Cancelled` result, exit `1`, and no Deploy launch. |
| No network before Connect | Disconnect networking before launching Bootstrap, with Connect already provisioned. | Connect still opens from local content. Its own connectivity checks may wait or fail; cached content does not guarantee a fully offline deployment. |
| Recoverable preparation problem | Observe a machine with unavailable optional WLAN or unavailable time services. | Applicable warning is readable; the workflow continues when Connect succeeds. Expected absence of optional WLAN need not produce a warning. |
| Missing explicit archive | Before launch, set `FOUNDRY_CONNECT_ARCHIVE=X:\missing-bootstrap-test.zip`. | Failure at the Connect stage, exit `1`, no application launch, session and log location displayed. Reboot to clear the override. |
| Debug content | Use media whose generated provisioning-source files contain `debug`. | Normal GitHub runtime release lookup is skipped. Inspect logs; an explicitly configured HTTP archive override still requests that archive. |
| USB persistence | Run against a media volume labelled `Foundry Cache`. | Logs are copied best effort to `<cache-drive>:\Logs\<session-id>`. Check actual files, not only the console result. |
| ISO layout | Test ISO media without an attached `Foundry Cache` volume. | Runtime content uses `X:\Foundry\Runtime`; no stale cache-persistence directory is inherited. |

Repeat normal startup and cancellation on x64 and ARM64 where available. Also inspect the console at its normal WinPE width, during a slow download, while Connect is in the foreground, and on an error. A final launch message is not a readiness acknowledgement; if Deploy immediately crashes, inspect its own log too.

For an optional checksum test, bring a valid matching Connect ZIP, set `FOUNDRY_CONNECT_ARCHIVE` to its local path, and set `FOUNDRY_CONNECT_ARCHIVE_SHA256` to an incorrect 64-digit hex digest. Bootstrap must fail before launching Connect and must not replace the existing active cache. Reboot to clear the overrides. Cache rollback and extraction failure paths also have automated tests.

## Cold boot with a test image

On the technician workstation, use an elevated Deployment and Imaging Tools Environment. Work on a **copy** of the Foundry-generated `sources\boot.wim`, not the distributed original. Place the copied WIM at `C:\BootstrapTest\boot.wim` and the matching executable at `C:\BootstrapTest\Foundry.Bootstrap.exe`. Use a new empty mount directory. The example assumes image index `1`; inspect your WIM and use its WinPE index.

```bat
dism /Get-WimInfo /WimFile:C:\BootstrapTest\boot.wim
mkdir C:\BootstrapTest\Mount
dism /Mount-Image /ImageFile:C:\BootstrapTest\boot.wim /Index:1 /MountDir:C:\BootstrapTest\Mount
mkdir C:\BootstrapTest\Mount\Foundry\Bootstrap
copy /y C:\BootstrapTest\Foundry.Bootstrap.exe C:\BootstrapTest\Mount\Foundry\Bootstrap\Foundry.Bootstrap.exe
notepad C:\BootstrapTest\Mount\Windows\System32\startnet.cmd
```

In this mounted test image, keep `wpeinit` and any unrelated customization. Replace the existing line invoking `FoundryBootstrap.ps1` with:

```bat
X:\Foundry\Bootstrap\Foundry.Bootstrap.exe
echo BootstrapExitCode=%ERRORLEVEL%
```

Do not leave both bootstrap invocations enabled. Save the file and commit the image:

```bat
dism /Unmount-Image /MountDir:C:\BootstrapTest\Mount /Commit
```

Put this WIM into `sources\boot.wim` on the copied boot media. For ISO tests, rebuild a test ISO with the modified WIM using your existing media tooling. Boot it and repeat the normal and cancellation scenarios. Check first-launch extraction time and free temporary storage, service readiness, clock/timezone results, and the actual console. Do not automate disk deployment as part of this test.

The mounting and startup customization follow Microsoft's [WinPE customization procedure](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-mount-and-customize?view=windows-11).

## Evidence to keep

Record architecture, media type, executable build/commit, last stage, exit code, and diagnostic session ID. Preserve `X:\Foundry\Logs\FoundryBootstrap.log` and relevant `FoundryConnect.log` / `FoundryDeploy.log`, including rotations covering the failure. Copy logs before rebooting; `X:` is temporary storage.

Match the `[Foundry.Bootstrap]` context and the chosen session when reviewing the log: the quick test may leave both old PowerShell and new .NET entries in the same file. If execution fails before any Bootstrap banner or session log, record the exact Windows error and executable architecture; that is a startup/extraction failure outside the managed workflow.

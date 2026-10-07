# Deploy native imaging and servicing

Deploy uses the operating system's architecture-matching `DismApi.dll` and `wimgapi.dll` from System32. Live preflight checks the required exports before target preparation. Simulation does not load these libraries.

| Operation | Backend |
| --- | --- |
| Image metadata | Existing DISM API metadata reader |
| Ordinary WIM application, including Windows Setup Media source extraction | WIMGAPI |
| ESD and any other non-WIM container | `dism.exe /Apply-Image /CheckIntegrity` |
| Feature inventory and payload-preserving disables | DISM API |
| Feature enables with dependency/source options | `dism.exe /Enable-Feature /All /LimitAccess` |
| Driver and firmware INF injection; WinRE mount, injection and unmount | DISM API |
| Applied Windows edition query | `dism.exe /Get-CurrentEdition` |

Backend selection is explicit. A native failure does not replay the operation through the CLI. Split WIM application is not supported by this deployment path.

Servicing runs in an internal worker mode of the same Deploy executable. Each DISM servicing worker initializes DISM once with its operation-owned scratch and log paths, releases sessions and allocations, and shuts down before returning. This preserves the UI process's existing metadata reader lifetime and follows the [process-wide DISM initialization contract](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/disminitialize-function?view=windows-11). No additional media executable or configuration setting is required.

The parent never terminates a worker. When the caller cancels, the parent signals a named event: WIM application aborts from its next message callback, driver injection stops before the next INF, and a mount receives the event as its DISM cancel handle and is then inspected and discarded. Feature changes, inventories and unmounts always run to completion. Cancellation surfaces only after the worker has finished native execution and cleanup, so an unresponsive native call cannot be interrupted. Native callbacks contain managed exceptions; progress-consumer failures are surfaced after resource release.

Driver progress counts completed INF files because `DismAddDriver` has no progress callback. Recursive discovery enforces signed-driver policy and does not follow reparse points outside the staged payload. Like `dism.exe /Add-Driver` on a folder, which [ignores INF files that are not valid driver packages](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-driver-servicing-command-line-options-s14?view=windows-11), injection continues past an INF that DISM rejects. Each rejection is logged as a warning, and the operation fails only when no INF could be added.

WIM application sets the temporary directory before loading the selected index and uses `WIM_FLAG_VERIFY` at [archive opening](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/wim/dd851934(v=msdn.10)?view=windows-11), matching the `/CheckIntegrity` behavior of the replaced command. [Application](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/wim/dd834965(v=msdn.10)?view=windows-11) does not add per-file verification. ESD remains on its established CLI path. Feature enables retain their CLI behavior because the public API documents modern Windows restrictions on [source and dependency options](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism/dismenablefeature-function?view=windows-11).

WinRE servicing refuses a preexisting registration at the owned mount path. It commits after successful injection and discards after failure, with a cleanup token independent of caller cancellation. Mount directories are deleted only after registration inventory confirms that they are absent. A remaining registration or unreadable inventory retains the directory and the original failure; diagnose registrations before manual recovery. After a successful commit, an unreadable inventory or a directory that cannot be removed is logged as a warning and does not fail the step.

Native logs are written to `Logs/Native/Dism.log` and `Logs/Native/Wimgapi.log` beneath the target Foundry staging workspace, outside disposable scratch. Final diagnostic handoff copies these logs even when an earlier handoff failed and the active log-session workspace remains elsewhere. Native status codes and cleanup diagnostics are preserved separately from worker process exit codes.

Unit tests cover orchestration and native ownership through injectable boundaries. Full deployment acceptance requires disposable x64 and ARM64 WinPE targets, representative WIM and ESD images, signed driver payloads, feature sources, and WinRE commit/discard checks.

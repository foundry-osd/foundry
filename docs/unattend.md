# Custom Windows answer files

Foundry can embed multiple custom Unattend XML files in deployment media. At deployment time, select one file or choose **Use Foundry settings**. Files are alternatives; Foundry does not merge them.

## Prepare media

1. Open **Customization > Unattend** and add one or more XML files.
2. Review each file's validation result. You can rename its display label, refresh a changed source, or remove it.
3. Enable custom answer files and choose the default, or keep **Use Foundry settings** as the default.
4. Enable deployment media password protection and enter its password.
5. Create ISO or USB media.

The saved Foundry configuration contains source references and content fingerprints, not the XML. Keep the original files available until you build media. After editing a source file, refresh it in Foundry; a changed or missing source blocks media generation. The finished boot image contains encrypted copies and no longer needs the original sources.

Disabling the feature keeps your authoring catalog but omits its assets from newly generated media. Native Foundry settings must remain valid because deployment operators can still select them.

## Deploy

Unlock the media and choose the answer file on **Target**, before Computer name. For a custom file, computer name and time zone are managed by that file. The deployment summary identifies the active choice.

Foundry validates the selected file and image architecture before preparing the target disk. Missing, changed, invalid, or incompatible selected files block deployment. Foundry never silently switches to another file or native settings.

After applying Windows, Foundry writes the original validated XML bytes to `Windows\Panther\unattend.xml` on the target. Switching to **Use Foundry settings** restores the normal naming and customization behavior.

## Supported settings

The first version supports the `specialize` and `oobeSystem` configuration passes. These cover settings such as computer name, time zone, local accounts, AutoLogon, OOBE, and commands valid in those passes.

Foundry applies Windows using DISM, rather than running setup.exe. Nonempty `windowsPE`, `offlineServicing`, `generalize`, `auditSystem`, and `auditUser` sections and root-level `servicing` instructions are therefore rejected, as is explicit audit-mode resealing. A full Windows Setup Autounattend.xml may need to be adapted by its author before importing. Unsupported sections are not silently removed.

Use settings appropriate to the target Windows architecture, edition, and version. Foundry does not convert amd64 components into arm64 components or install missing language resources. XML validation is not a substitute for [Windows SIM validation](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/wsim/best-practices-for-authoring-answer-files) and a representative VM deployment.

The parser prohibits DTDs and external entity resolution and limits each file to 4 MiB. Extension content and XML encoding are preserved. Foundry does not run imported commands in WinPE or import scripts merely because the XML references them. Arrange external script availability yourself.

Automatic USB-root answer-file discovery and runtime USB browsing are not included; import files while preparing the boot image.

## Which settings take precedence?

| Area | With a custom file selected |
| --- | --- |
| Computer name and time zone | Controlled by the custom file; omissions use Windows/image defaults. |
| Foundry OOBE, privacy policies, local accounts, Administrator setup | Suppressed, including associated registry writes and runtime account-password processing. |
| Disk layout, OS selection, downloads, recovery, firmware, offline drivers | Continue to use Foundry's deployment choices. |
| Optional features, AppX/AI removal, deferred drivers, network roaming | Remain configured by Foundry. Custom commands can interfere with these operations. |
| Autopilot JSON and interactive registration | Explicit account/OOBE/domain-join conflicts block deployment. Choose a compatible answer file or change the authored provisioning configuration. |
| Hardware hash upload from WinPE | Registration can continue. Upload success does not guarantee enrollment through the subsequent customized OOBE. |

Custom mode owns the entire naming/OOBE area even when the file omits individual values. This prevents Foundry from unexpectedly inserting accounts, overwriting user settings, or applying conflicting privacy policies.

Foundry detects known incompatible XML settings; it cannot prove the effects of arbitrary scripts. A script can alter policies, remove required packages, change enrollment behavior, or replace Foundry's setup hooks. Validate the complete combination in a VM.

Some Foundry customizations use `SetupComplete.cmd`. Microsoft documents restrictions for OEM product keys on affected editions and warns against rebooting from that hook. Changing ProductKey or interfering with setup scripts can therefore affect these features. [Windows setup script behavior](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/add-a-custom-script-to-windows-setup?view=windows-11)

`FirstLogonCommands` also does not guarantee that one command finishes before the next begins. Put dependent actions in a single script when sequencing matters. [Microsoft command-order documentation](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup-firstlogoncommands-synchronouscommand-order)

## Sensitive content and cleanup

Password protection is required for every embedded custom file, including files that appear to contain no credentials. The whole file is encrypted with Foundry's existing deployment-media key. Raw XML and command bodies are excluded from Foundry logs and support output.

Windows needs the decrypted file on the target before first boot. Protect your source files and deployed computers accordingly. Password hiding in Windows SIM is not comprehensive encryption, and Windows cannot be assumed to scrub arbitrary secrets embedded in custom commands or extensions.

Do not delete the target Panther answer file before `oobeSystem` has consumed it. Arrange cleanup after the required setup passes through your deployment process; Foundry preserves your XML and does not inject a cleanup command. [Microsoft answer-file lifecycle guidance](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-setup-automation-overview?view=windows-11)

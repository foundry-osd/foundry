# Findings & Decisions

## Requirements
- Definir un plan de refonte Foundry pour la partie partitionnement/deploiement image en capitalisant sur OSDCloud.
- Priorite immediate: ajouter une partition de recuperation fonctionnelle en V1.
- Produire le plan uniquement, sans implementation dans ce tour.

## Research Findings
- Le repo courant (`Foundry`) n'est pas `OSDCloud`; les sources OSDCloud doivent etre inspectees separement.
- Le workflow de planning est initialise localement avec fichiers de suivi.
- Le workflow principal `OSDCloud` execute: `Clear Local Disk` puis `Partition Local Disk`, puis `Download Windows ESD`, `Select Windows Image Index`, `Expand Windows Image to Local Disk` (workflow/default/tasks/osdcloud.json).
- Le nettoyage disque est fait via `step-preinstall-cleartargetdisk` -> `Clear-DeviceLocalDisk -Force -Confirm:$Confirm`, et `Clear-DeviceLocalDisk` execute `diskpart clean` via `Invoke-DiskpartClean`.
- Le partitionnement est declenche via `step-preinstall-partitiontargetdisk` -> `New-OSDCloudDisk -PartitionStyle GPT ...` (donc GPT force dans ce workflow).
- `New-OSDCloudDisk` initialise le disque cible, cree la partition systeme via `New-OSDCloudPartitionSystem`, puis cree la/les partition(s) Windows/Recovery via `New-OSDCloudPartitionWindows`.
- Schema GPT observe:
  - System: 260MB par defaut, FAT32, lettre `S`, type EFI.
  - MSR: 16MB par defaut.
  - Windows: NTFS lettre `C`, taille max ou `LargestFreeExtent - SizeRecovery`.
  - Recovery (optionnelle): GPT recovery, NTFS lettre `R`, attribut GPT `0x8000000000000001`.
- La partition Recovery est active par defaut, desactivee en VM (`$IsVM`) ou via option Skip; dans le code actuel les parametres `RecoveryPartitionForce/Skip` sont typés `string` dans la step.
- Cote image OS, `step-install-downloadwindowsimage` telecharge l'image (ESD) et fixe `WindowsImagePath`.
- `step-install-getwindowsimageindex` determine l'index (single index, ImageName, EditionId, sinon prompt manuel).
- `step-install-expandwindowsimage` applique l'image avec `Expand-WindowsImage` sur `ApplyPath = 'C:\'` avec `ImagePath = WindowsImagePath`, `Index = WindowsImageIndex`, `ScratchDirectory = 'C:\OSDCloud\Temp'`.
- L'application se fait seulement en WinPE (`if ($IsWinPE -eq $true)`).
- Le module charge recursivement tous les scripts `private\*.ps1` (dont `private/dev/*`) depuis `OSDCloud.psm1`, donc les fonctions inspectees sont bien actives.
- Dans `Foundry.Deploy`, l'orchestration est centralisee dans `DeploymentOrchestrator` avec 10 etapes, dont `Prepare target disk layout`, `Download operating system image`, `Apply operating system image`.
- Le partitionnement est implemente en C# dans `WindowsDeploymentService.PrepareTargetDiskAsync` via un script `diskpart` genere a la volee:
  - `clean`
  - `convert gpt`
  - `create partition efi size=260` (FAT32, label System)
  - `create partition msr size=16`
  - `create partition primary` (NTFS, label Windows)
  - assignation dynamique de lettres (systeme + windows)
- Contrairement au workflow OSDCloud analyse precedemment, il n'y a pas de creation de partition Recovery dans ce flux `Foundry.Deploy`.
- Le style de partition est force en GPT dans le script `diskpart` (`convert gpt`), sans branche MBR.
- La resolution d'index image est faite par `dism /Get-ImageInfo`, parsee via regex (`Index`, `Name`, `Edition`, `Edition ID`) puis matching sur l'edition demandee.
- L'application image est faite par `dism /Apply-Image /ImageFile:\"...\" /Index:n /ApplyDir:\"...\" /CheckIntegrity /ScratchDir:\"...\"`.
- La configuration boot est ensuite appliquee via `bcdboot "<WindowsPath>" /s "<SystemPartition>" /f UEFI`.
- Le telechargement image passe par `ArtifactDownloadService` (HttpClient + retry + verif hash SHA1/SHA256 + cache hit).
- Le disque cible est revalide avant les etapes destructives (`ValidateTargetDiskSelectionAsync`) et bloque si system/boot/read-only/offline.
- Protection supplementaire: blocage si le cache se trouve sur le meme disque que la cible (`AdjustCacheForTargetDiskConflictAsync`).
- Le mode debug (`IsDryRun`) simule toutes les etapes sans actions destructives; une cible virtuelle (Disk 999) est injectee cote UI.
- Decisions V1 validees avec le user:
  - Recovery: toujours creee.
  - Firmware scope: UEFI/GPT uniquement.
  - UI: pas de nouvelle option Recovery en V1.
  - Taille Recovery: 990MB.
  - Visibilite: partition cachee sans lettre en fin de workflow.
  - Comportement: echec bloquant si la chaine Recovery/WinRE n'est pas validee.
  - Direction: best-practice Microsoft (configuration WinRE explicite), pas strictement "OSDCloud pur".
- OSDCloud cree correctement la partition Recovery (GUID + attributs) mais ne montre pas dans ce repo une sequence explicite `reagentc /setreimage` + validation `reagentc /info`.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Inspection basee sur code PowerShell OSDCloud | Source la plus fiable pour comprendre le comportement reel |
| Produire un mapping fonctionnel (fonction -> role -> ordre d'appel) | Reponse directement exploitable pour la suite |
| Se concentrer sur workflow `default` | C'est le flux deploye par defaut pour `Deploy-OSDCloud` |
| Produire un plan decision-complete avant implementation | Exigence user explicite |
| Cibler d'abord `WindowsDeploymentService` et `DeploymentOrchestrator` | Ce sont les points d'orchestration et commandes systeme |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Script `session-catchup.py` non executable faute de runtime Python | Reprise manuelle de contexte via fichiers de planification |

## Resources
- https://github.com/OSDeploy/OSDCloud
- c:\DEV\Github\OSDCloud\workflow\default\tasks\osdcloud.json
- c:\DEV\Github\OSDCloud\private\steps\3-preinstall\step-preinstall-cleartargetdisk.ps1
- c:\DEV\Github\OSDCloud\private\steps\3-preinstall\step-preinstall-partitiontargetdisk.ps1
- c:\DEV\Github\OSDCloud\private\dev\Disk.ps1
- c:\DEV\Github\OSDCloud\private\dev\New-OSDCloudPartitionSystem.ps1
- c:\DEV\Github\OSDCloud\private\dev\New-OSDCloudPartitionWindows.ps1
- c:\DEV\Github\OSDCloud\private\steps\4-install\step-install-downloadwindowsimage.ps1
- c:\DEV\Github\OSDCloud\private\steps\4-install\step-install-getwindowsimageindex.ps1
- c:\DEV\Github\OSDCloud\private\steps\4-install\step-install-expandwindowsimage.ps1
- c:\DEV\Github\OSDCloud\OSDCloud.psm1
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Deployment\DeploymentOrchestrator.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Deployment\WindowsDeploymentService.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Deployment\IWindowsDeploymentService.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Deployment\DeploymentTargetLayout.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Hardware\TargetDiskService.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\Services\Download\ArtifactDownloadService.cs
- c:\DEV\Github\Foundry\src\Foundry.Deploy\ViewModels\MainWindowViewModel.cs
- `task_plan.md`
- `progress.md`

## Visual/Browser Findings
- Aucune consultation navigateur detaillee enregistree pour le moment.

---
*Update this file after every 2 view/browser/search operations*
*This prevents visual information from being lost*

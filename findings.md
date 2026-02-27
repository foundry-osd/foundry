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

## Implementation Findings
- `DeploymentTargetLayout` expose maintenant `RecoveryPartitionRoot` et `RecoveryPartitionLetter`, ce qui permet a l'orchestrateur de piloter explicitement la partition Recovery.
- `DeploymentRuntimeState` conserve `TargetRecoveryPartitionRoot`, `TargetRecoveryPartitionLetter`, `WinReConfigured` et `WinReInfoOutputPath`, ce qui rend la phase Recovery observable dans les logs finaux.
- `IWindowsDeploymentService` a ete etendu avec deux operations dediees:
  - `ConfigureRecoveryEnvironmentAsync`
  - `SealRecoveryPartitionAsync`
- `WindowsDeploymentService.PrepareTargetDiskAsync` cree maintenant un layout GPT en 4 partitions:
  - EFI 260MB
  - MSR 16MB
  - Windows primaire NTFS
  - Recovery 990MB NTFS
- Le script `diskpart` applique egalement le type Recovery Microsoft (`de94bba4-06d1-4d40-a16a-bfd50179d6ac`) et les attributs GPT `0x8000000000000001`, ce qui recupere le bon comportement structurel vu dans OSDCloud.
- La configuration WinRE suit une approche plus robuste qu'OSDCloud:
  - copie de `winre.wim` depuis l'image offline appliquee
  - `reagentc /setreimage`
  - `reagentc /enable`
  - `reagentc /info`
  - validation bloquante du statut `Enabled` et d'un chemin contenant `Recovery\\WindowsRE`
- La partition Recovery est ensuite masquee en retirant sa lettre temporaire via un second script `diskpart`.
- `DeploymentOrchestrator` a ete etendu avec une etape explicite `Configure recovery environment` entre l'application de l'image OS et l'injection des drivers offline.
- Le dry-run reste coherent avec ce nouveau workflow:
  - creation d'une racine Recovery simulee
  - production d'un `reagentc-info.txt` simule
  - mise a jour de `WinReConfigured`
- Le diagnostic `reagentc-info.txt` est persiste dans `logSession.StateDirectoryPath`, ce qui evite de le perdre lorsque `TargetFoundryRoot` est nettoye en fin de run.
- Verification de compilation effectuee:
  - `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj`
  - resultat: `0 Warning(s)`, `0 Error(s)`

## Alignment Pass Findings
- Un second passage a ete implemente pour recuperer trois points que OSDCloud couvrait mieux dans son workflow:
  - servicing du vrai `winre.wim`,
  - options `bcdboot` plus robustes,
  - verification post-apply de l'edition offline.
- `IWindowsDeploymentService` expose maintenant:
  - `GetAppliedWindowsEditionAsync`
  - `ApplyRecoveryDriversAsync`
  - une signature `ConfigureBootAsync` enrichie avec `operatingSystemBuildMajor`
- `WindowsDeploymentService.GetAppliedWindowsEditionAsync` interroge l'image offline via `dism /Image:<root> /Get-CurrentEdition` et parse `Current Edition`.
- `WindowsDeploymentService.ConfigureBootAsync` aligne la logique sur OSDCloud:
  - builds < 26200: `bcdboot ... /c /v`
  - builds >= 26200: `bcdboot ... /c /bootex`
- `WindowsDeploymentService.ApplyRecoveryDriversAsync` service maintenant le WinRE actif de la partition Recovery:
  - monte `Recovery\\WindowsRE\\winre.wim`
  - injecte les drivers offline via `dism /Add-Driver`
  - demonte avec `Commit` en cas de succes, `Discard` sinon
  - tente un cleanup du point de montage
- `DeploymentOrchestrator` a ete ajuste:
  - verification non bloquante de l'edition offline juste apres `ApplyImageAsync`
  - injection des drivers dans Windows puis dans WinRE si `WinReConfigured`
  - nouvelle etape explicite `Seal recovery partition`
  - scellement de Recovery deplace apres le servicing drivers, ce qui corrige le trou fonctionnel initial
- Le dry-run a ete mis a jour pour refleter:
  - verification simulee de l'edition appliquee
  - injection simulee Windows + WinRE
  - nouvelle etape `Seal recovery partition`
- Verification de compilation apres ce second passage:
  - `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj`
  - resultat: `0 Warning(s)`, `0 Error(s)`

## Context7 Notes
- Documentation Microsoft consultee via Context7 (`/microsoftdocs/windows-driver-docs`) pour verifier l'alignement avec les recommandations UEFI/GPT et la configuration offline de WinRE.
- Direction retenue: conserver les bons elements structurels d'OSDCloud (partition Recovery correctement typée) tout en rendant la configuration WinRE explicite et verifiable dans Foundry.
- Second usage Context7 pour recouper:
  - la logique `bcdboot` en contexte UEFI
  - le pattern DISM de servicing offline (mount/add-driver/unmount) applique a `winre.wim`

## Remaining Risks
- La syntaxe `diskpart` retenue (`set id=\"GUID\"` sur la partition 4) compile cote code mais doit etre validee en execution WinPE reelle sur une machine cible.
- Le layout suppose que la partition Recovery finale reste la partition 4 sur un disque nettoye (`clean` + `convert gpt`), ce qui est coherent ici mais doit etre confirme en test terrain.
- Aucun test unitaire/integration n'a ete ajoute dans ce cycle; seule la compilation a ete verifiee.
- Le parsing `Current Edition` depend de la sortie textuelle DISM en anglais (`/English`) et doit etre confirme sur l'environnement cible.
- Le servicing WinRE suppose que `winre.wim` est present et montable directement depuis `Recovery\\WindowsRE\\winre.wim`, ce qui doit etre confirme en test WinPE reel.

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

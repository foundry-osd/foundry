# Findings & Decisions

## Requirements
- Analyser les erreurs présentes dans `f:\FoundryDeploy.log`.
- Expliquer la cause.
- Comparer avec la gestion équivalente dans OSDCloud (`https://github.com/OSDeploy/OSDCloud`).

## Research Findings
- Le log montre deux anomalies principales:
- `DISM /Image:"W:\" /Get-CurrentEdition` échoue avec `ExitCode=1639`, mais le flux continue avec un warning.
- `reagentc.exe` échoue au démarrage avec une `Win32Exception (2)` car l'exécutable n'est pas trouvé; cette erreur stoppe le déploiement.
- Dans le code local, `GetAppliedWindowsEditionAsync` utilise `RunRequiredProcessAsync`, puis l'appelant journalise un warning et continue.
- Dans le code local, `ConfigureRecoveryEnvironmentAsync` lance directement `reagentc.exe`; si l'exécutable est absent, l'exception remonte et devient bloquante.
- La commande DISM locale est construite sous forme de chaîne brute avec `startInfo.Arguments`, pas via `ArgumentList`.
- Avec `windowsPartitionRoot = "W:\\"`, l'argument produit est `/Image:"W:\"`; le backslash terminal avant le guillemet fermant est un cas classique de casse du parsing Windows pour les lignes de commande brutes.
- La cause la plus probable du `1639` est donc un problème de quoting, pas une commande DISM intrinsèquement invalide.
- Dans OSDCloud, l'application de l'image se fait via `Expand-WindowsImage`, pas via un appel manuel à `dism.exe /Apply-Image`.
- Dans OSDCloud, le boot se configure avec `C:\Windows\System32\bcdboot.exe` après application de l'image.
- Dans OSDCloud, la vérification de l'édition passe par `Get-WindowsEdition -Path 'C:\'` dans un `try/catch`; en cas d'échec, le step émet `Unable to get Windows Edition. OK.` et continue.
- Dans OSDCloud, sur VM, le partitionnement désactive par défaut la partition Recovery (`if ($IsVM -eq $true) { $RecoveryPartition = $false }`).
- Dans OSDCloud, la gestion WinRE trouvée jusqu'ici passe par la présence de `C:\Windows\System32\Recovery\winre.wim`, avec gardes `Test-Path` et plusieurs `-ErrorAction SilentlyContinue`, sans usage de `reagentc.exe` sur ce flux.
- L'orchestrateur local revalide déjà le disque cible avant partitionnement; il peut donc fournir `SizeBytes` au service de déploiement.
- Le chemin le plus propre pour l'alignement OSDCloud est donc de calculer la taille de la partition Windows côté C# et de supprimer `shrink`.
- Pour WinRE, un fallback de "staging" (copie de `winre.wim` + rapport synthétique) permet de conserver la partition de récupération obligatoire, tout en évitant un échec fatal en WinPE.
- Sur demande utilisateur, le fallback a été retiré lui aussi: Foundry n'utilise désormais plus du tout `reagentc`.
- Foundry traite maintenant `winre.wim` comme OSDCloud: le fichier reste sous `Windows\\System32\\Recovery`, le rapport de statut est purement informatif, et l'injection de drivers WinRE cible cette image offline.
- Le step "Seal recovery partition" a été converti en no-op pour laisser la partition montée pendant la session, comme dans le flux OSDCloud observé.
- La doc Microsoft `REAgentC` montre que `/setreimage` prend le répertoire qui contient `winre.wim`; l'exemple offline officiel utilise un chemin de partition de récupération du type `T:\\Recovery\\WindowsRE` avec `/target W:\\Windows`.
- Avec cette contrainte, `winre.wim` doit bien être copié dans la partition de récupération avant `reagentc /setreimage`.
- La doc indique aussi l'usage de `/enable /osguid <GUID>` depuis Windows PE après `bcdboot`; le GUID doit donc être résolu depuis le store BCD cible.
- La doc Microsoft sur les composants optionnels WinPE indique que `WinPE-WinReCfg` fournit `winrecfg.exe`; c'est l'outil prévu dans WinPE pour configurer WinRE sans embarquer `reagentc.exe`.
- Context7 sur la doc Microsoft Windows n'a pas remonté d'extrait exploitable sur `winrecfg.exe`; la décision repose donc sur la doc Microsoft déjà identifiée et sur le code local.
- Le flux local a été aligné sur cette contrainte: `winre.wim` reste copié vers `Recovery\\WindowsRE`, puis `winrecfg.exe` est appelé sans fallback pour `/setreimage`, `/enable /osguid`, et `/info /target`.
- Si `winrecfg.exe` est absent du WinPE courant, Foundry échoue désormais immédiatement avec un message explicite demandant d'ajouter `WinPE-WinReCfg`.
- Côté `src/Foundry`, le builder WinPE n'ajoutait pas initialement `WinPE-WinReCfg`; la liste de composants optionnels contenait PowerShell/WMI/NetFX/etc. mais pas ce CAB.
- Le builder WinPE ajoute maintenant explicitement `WinPE-WinReCfg`, ce qui met `winrecfg.exe` dans le `boot.wim` généré par Foundry.
- `winre-config-info.txt` n'était pas utilisé fonctionnellement; il a été supprimé du flux, ainsi que le champ de runtime et l'export de summary associés.
- Dans le log du 2 mars 2026, l'échec `diskpart` survient après la création et le formatage de la partition Windows: c'est la création de la partition Recovery 2048 MB qui échoue avec `Espace libre utilisable introuvable`.
- La cause est le calcul de taille dans `PrepareTargetDiskAsync`: `diskSizeBytes` vaut exactement `130048 MiB`, et la formule réserve seulement `1 MiB` de buffer GPT. Avec `260 + 16 + 127723 + 2048 = 130047 MiB`, le placement aligné de la première partition consomme déjà le seul MiB de marge; il ne reste donc plus assez d'espace pour la partition Recovery plus la fin de disque GPT.
- Les messages `Le disque est déjà en ligne` et `DiskPart n'a pas pu effacer les attributs de disque` sont secondaires dans ce log: le script continue et le blocage réel est bien le manque d'espace libre au dernier `create partition primary size=2048`.
- Le correctif retenu est de laisser DiskPart créer la partition Windows à la taille maximale, puis de faire `shrink desired=2048 minimum=2048` avant de créer la partition Recovery. Cela supprime le pré-calcul C# de taille de partition Windows, qui était la source du décalage.
- Dans `FoundryDeploy2.log` (2 mars 2026), `diskpart` réussit et l'échec suivant survient à l'étape `Configure recovery environment`.
- Le message d'aide affiché par `WINRECFG.EXE` avec `ExitCode=87` montre que la commande passée est invalide pour cet outil. Le log indique explicitement `Failed to enable Windows RE`, et le code appelle encore `winrecfg.exe /enable /osguid <GUID>`.
- Or `WINRECFG.EXE` expose ici seulement `/info`, `/setreimage` et `/setbootshelllink`; il ne supporte pas `/enable`. Le portage direct depuis `reagentc.exe` est donc incorrect.
- `winrecfg /setreimage` a vraisemblablement réussi dans ce run, puisque l'exception n'apparaît qu'au second appel, sur l'étape marquée `Failed to enable Windows RE`.
- Le correctif appliqué supprime l'appel `winrecfg.exe /enable /osguid ...`, retire la résolution du GUID BCD et supprime le paramètre `systemPartitionRoot` de l'étape `ConfigureRecoveryEnvironmentAsync`, car il n'est plus nécessaire dans ce flux.
- Dans le dernier `FoundryDeploy.log` (2 mars 2026, ~05:51 UTC), `WINRECFG.EXE` ne renvoie plus d'erreur de commande: `WINRECFG.EXE : opération réussie.`
- Le nouvel échec vient de `ValidateRecoveryConfiguration`, qui exige que la sortie de `winrecfg /info /target W:\\Windows` contienne `R:\\Recovery\\WindowsRE`.
- Or la sortie réelle indique `État WinRE : Disabled` et `Emplacement WinRE :` vide. Cela signifie que `winrecfg /setreimage` a accepté la commande, mais que `winrecfg /info` ne reflète pas une activation/mise en correspondance effective vers la partition Recovery dans ce contexte WinPE.
- Le blocage actuel est donc une hypothèse métier locale trop forte (validation par présence du chemin), pas un échec du binaire `winrecfg.exe`.
- Le flux de reboot final appelait encore `shutdown.exe /r /t 0 /f` dans `MainWindowViewModel`. En WinPE, il faut cibler `wpeutil.exe Reboot`, et le code ne journalisait pas explicitement les échecs de code de retour.
- Le correctif appliqué choisit `wpeutil.exe Reboot` en WinPE, conserve `shutdown.exe` hors WinPE, et journalise maintenant explicitement le code de retour ainsi que `StdOut`/`StdErr` si la commande de redémarrage échoue.
- Sur demande utilisateur, ce flux a été simplifié davantage: `MainWindowViewModel` n'utilise plus aucune logique de sélection et exécute uniquement `wpeutil.exe Reboot`.
- Les logs spécifiques ajoutés pour le reboot ont été retirés; seul le logging générique du `ProcessRunner` subsiste si le niveau `Debug`/`Verbose` reste activé.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Distinguer erreurs non bloquantes et bloquantes | Le log montre explicitement un warning suivi d'une erreur fatale |
| Basculer `GetAppliedWindowsEditionAsync` vers `ArgumentList` | Supprime le risque de quoting sur `W:\` et reste minimal |
| Faire de `reagentc` une optimisation et non un prérequis | C'est le moyen d'aligner Foundry sur la robustesse d'OSDCloud sans supprimer la partition Recovery |
| Supprimer toute la logique `reagentc` après validation | Plus simple, plus cohérent avec OSDCloud, et conforme à la demande utilisateur |
| Réintroduire `reagentc` avec `osguid` | C'est la voie correcte si l'objectif est une partition Recovery réellement utilisée par WinRE |
| Remplacer `reagentc.exe` par `winrecfg.exe` sans fallback | L'utilisateur veut une stratégie WinPE propre, centrée sur `WinPE-WinReCfg`, sans chemin de secours implicite |
| Supprimer `winre-config-info.txt` | Ce fichier ne sert qu'au reporting et ajoute du bruit sans participer à la décision métier |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Le tail du log n'inclut pas forcément les lignes après l'exception | Suffisant pour l'analyse initiale; compléter si nécessaire |
| Une commande shell PowerShell composée a été refusée par la politique | Utilisation de commandes `git` séparées et simples |

## Resources
- `f:\FoundryDeploy.log`
- `e:\Github\Foundry\src\Foundry.Deploy\Services\Deployment\WindowsDeploymentService.cs`
- `e:\Github\Foundry\src\Foundry.Deploy\Services\Deployment\DeploymentOrchestrator.cs`
- `e:\Github\Foundry\src\Foundry.Deploy\Services\System\ProcessRunner.cs`
- `Context7: /websites/learn_microsoft_en-us_dotnet`
- `Context7: /dotnet/runtime`
- `https://github.com/OSDeploy/OSDCloud`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-expandwindowsimage.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-bcdboot.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-getwindowsedition.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\3-preinstall\step-preinstall-partitiontargetdisk.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\5-drivers\step-drivers-recast-winre.ps1`
- `https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-windows-edition-servicing-command-line-options?view=windows-11`
- `https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsedition?view=windowsserver2025-ps`
- `https://learn.microsoft.com/fr-fr/windows-hardware/manufacture/desktop/reagentc-command-line-options?view=windows-11`

## Visual/Browser Findings
- Aucun pour l'instant.

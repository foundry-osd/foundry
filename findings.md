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

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Distinguer erreurs non bloquantes et bloquantes | Le log montre explicitement un warning suivi d'une erreur fatale |
| Basculer `GetAppliedWindowsEditionAsync` vers `ArgumentList` | Supprime le risque de quoting sur `W:\` et reste minimal |

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
- `https://github.com/OSDeploy/OSDCloud`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-expandwindowsimage.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-bcdboot.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\4-install\step-install-getwindowsedition.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\3-preinstall\step-preinstall-partitiontargetdisk.ps1`
- `C:\Users\mchav\AppData\Local\Temp\OSDCloud-main-20260301\private\steps\5-drivers\step-drivers-recast-winre.ps1`
- `https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-windows-edition-servicing-command-line-options?view=windows-11`
- `https://learn.microsoft.com/en-us/powershell/module/dism/get-windowsedition?view=windowsserver2025-ps`

## Visual/Browser Findings
- Aucun pour l'instant.

# Findings & Decisions

## Requirements
- Fournir une analyse approfondie des working directories utilises par `Foundry.Deploy` en WinPE.
- Decrire le sequencage des steps de deploiement.
- Appuyer l analyse sur le code source du repo (scripts WinPE + code C#).

## Research Findings
- `FoundryBootstrap.ps1` fixe `X:\Foundry` comme base WinPE, detecte eventuellement une partition cache USB (`VolumeLabel` ou dossier `Foundry Cache`) et resolve un `BootstrapRoot` vers `<Drive>:\Runtime` ou `X:\Foundry\Runtime`.
- Le bootstrap telecharge/copie `Foundry.Deploy.zip` dans `<BootstrapRoot>\Foundry.Deploy.zip`, extrait dans `<BootstrapRoot>\current`, puis lance `Foundry.Deploy.exe` avec `WorkingDirectory = $Executable.DirectoryName`.
- Dans `MainWindowViewModel`, `CacheRootPath` est initialise a `X:\Foundry\Runtime` et reforce a cette valeur au demarrage de deploiement (hors debug), avant creation du `DeploymentContext`.
- Le sequencage d execution runtime est porte par `DeploymentOrchestrator` via 10 steps ordonnes, emis en `StepProgressChanged`.
- `ProcessRunner` impose un `workingDirectory` non vide, cree le dossier, puis execute tous les processus externes avec ce repertoire explicite.
- Les repertoires runtime de base sont crees par `EnsureCacheFolders(runtimeRoot)`: `Extracted\Drivers`, `Logs`, `State`, `Temp\Deployment`, `Temp\Dism`, `Autopilot`.
- Les caches `OperatingSystem` et `DriverPack` sont crees au niveau `cacheBaseRoot` (parent si `runtimeRoot` se termine par `Runtime`). Exemple: `D:\Runtime` => caches en `D:\OperatingSystem` et `D:\DriverPack`.
- En mode ISO, apres preparation disque, les caches OS/Driver sont rediriges vers `TargetFoundryRoot` (`<WindowsPartition>\Foundry\OperatingSystem|DriverPack`).
- Les logs changent potentiellement de root 2 fois: initialisation (requested root), resolution cache (rebind possible), puis rebascule vers `TargetFoundryRoot` apres partitionnement.
- Fin de sequence: artefacts finaux copies vers `<WindowsPartition>\Windows\Temp\Foundry` (Logs, State, summary), puis nettoyage best-effort de `<WindowsPartition>\Foundry` si distinct.
- En cas de conflit cache/target disk, fallback vers `X:\Foundry\Runtime\IsoConflict` (ou `%TEMP%\Foundry\Runtime\IsoConflict` si creation impossible sur X:).

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Tracer le flux en partant du bootstrap WinPE vers le ViewModel principal | Permet d observer la propagation des paths et l ordre d execution |
| Identifier tous les points de mutation de repertoire | Evite une lecture superficielle du seul point d entree |
| Distinguer `runtimeRoot` et `cacheBaseRoot` dans l analyse | Point cle pour comprendre ou OS/Driver sont telecharges |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Scripts de catchup non executables sans interpreteur Python | Initialisation manuelle des fichiers de suivi |

## Resources
- `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`
- `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
- `src/Foundry.Deploy/ViewModels/DeploymentStepItemViewModel.cs`
- `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
- `src/Foundry.Deploy/Services/Cache/CacheLocatorService.cs`
- `src/Foundry.Deploy/Services/Deployment/WindowsDeploymentService.cs`
- `src/Foundry.Deploy/Services/System/ProcessRunner.cs`

## Visual/Browser Findings
- N/A (analyse locale de code)

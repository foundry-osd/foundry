# Findings & Decisions

## Requirements
- Plan uniquement pour le moment (pas d implementation immediate dans cette demande).
- Migrer le logging de `Foundry` et `Foundry.Deploy` vers Serilog.
- Interdiction de code partage entre les deux implementations.
- Ecrire les logs en fichier.
- Voir les logs dans la sortie Debug de Visual Studio.
- Remplacer la logique de logging existante par Serilog.
- Couverture maximale des niveaux de log (du detail au critique).
- Horodatage demande: UTC ISO-8601.
- Nom du fichier central Deploy en WinPE: `X:\Foundry\Logs\FoundryDeploy.log`.

## Research Findings
- `Foundry` loggue actuellement via `Debug.WriteLine` dans `src/Foundry/ViewModels/MainWindowViewModel.cs`.
- `Foundry.Deploy` utilise un startup log manuel dans `src/Foundry.Deploy/Program.cs`.
- `Foundry.Deploy` contient un sous-systeme custom:
  - `src/Foundry.Deploy/Services/Logging/IDeploymentLogService.cs`
  - `src/Foundry.Deploy/Services/Logging/DeploymentLogService.cs`
  - `src/Foundry.Deploy/Services/Logging/DeploymentLogSession.cs`
  - `src/Foundry.Deploy/Services/Logging/DeploymentLogLevel.cs`
- L orchestration Deploy gere un transfert de session log vers la cible et persiste un `deployment-state.json`.
- Le bootstrap WinPE `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1` ecrit deja un log en append; il faut l aligner sur `FoundryDeploy.log` pour la continuite.
- `Foundry.Deploy` expose actuellement des logs live UI (`DeploymentLogs`) via l event `LogEmitted` de l orchestrateur.
- Les extensions `AddSerilog` sur `ILoggingBuilder` fonctionnent avec `Serilog.Extensions.Logging` (validation compile OK).
- Les packages installes (dernieres stables resolues): `Serilog 4.3.1`, `Serilog.Extensions.Logging 10.0.0`, `Serilog.Sinks.File 7.0.0`, `Serilog.Sinks.Debug 3.0.0`.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Serilog configure separement dans chaque projet | Pas de base de code commune |
| Sinks: File + Debug uniquement | Besoin utilisateur exact |
| Niveau global Verbose | "Tout logger" |
| UTC ISO-8601 pour timestamps | Coherence technique entre bootstrap et exe |
| Foundry: un fichier par demarrage + retention 7 jours | Historique court et lisible |
| Foundry.Deploy: un fichier unique `FoundryDeploy.log` en append | Continuite bootstrap/exe et fonctionnement WinPE |
| `deployment-state.json` conserve | Ne pas casser la logique runtime actuelle |
| Suppression des logs live UI Deploy | Choix utilisateur explicite |
| Output template unifie: `UtcTimestamp | Level | SourceContext | Message | Exception` | Lisibilite + horodatage UTC ISO-8601 |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Erreur de build CS0103 (`Path`, `Directory`, `File`) dans nouveaux fichiers logging Deploy | Ajout de `using System.IO;` explicite puis rebuild OK |

## Resources
- Serilog docs (Context7 id): `/serilog/serilog`
- Fichiers cibles principaux:
  - `src/Foundry/Program.cs`
  - `src/Foundry/Logging/FoundryLogging.cs`
  - `src/Foundry/Logging/UtcTimestampEnricher.cs`
  - `src/Foundry/ViewModels/MainWindowViewModel.cs`
  - `src/Foundry.Deploy/Program.cs`
  - `src/Foundry.Deploy/Services/Logging/*`
  - `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
  - `src/Foundry.Deploy/Services/Deployment/IDeploymentOrchestrator.cs`
  - `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs`
  - `src/Foundry.Deploy/MainWindow.xaml`
  - `src/Foundry/Assets/WinPe/FoundryBootstrap.ps1`

## Visual/Browser Findings
- Aucune source visuelle externe necessaire pour cette phase.

---
*Mis a jour pour preparer l implementation sans ambiguite.*

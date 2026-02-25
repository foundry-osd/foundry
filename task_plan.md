# Task Plan: Migration du logging vers Serilog (Foundry + Foundry.Deploy)

## Goal
Definir un plan d implementation complet et decision-complete pour migrer le logging des projets `Foundry` et `Foundry.Deploy` vers Serilog, sans code partage entre projets.

## Current Phase
Phase 6

## Phases

### Phase 1: Requirements & Discovery
- [x] Comprendre le besoin utilisateur
- [x] Cartographier le logging existant dans `src/Foundry` et `src/Foundry.Deploy`
- [x] Documenter les constats dans `findings.md`
- **Status:** complete

### Phase 2: Planning & Structure
- [x] Verrouiller tous les choix fonctionnels et techniques (timestamp, sinks, retention, format)
- [x] Definir l architecture cible par projet (implementations separees)
- [x] Produire le plan detaille d implementation
- **Status:** complete

### Phase 3: Implementation - Foundry
- [x] Ajouter packages Serilog dedies a `Foundry`
- [x] Configurer Serilog (File + Debug, Verbose global, UTC ISO-8601)
- [x] Remplacer `Debug.WriteLine` par `ILogger<T>`
- [x] Ajouter hooks d exceptions globales
- **Status:** complete

### Phase 4: Implementation - Foundry.Deploy
- [x] Ajouter packages Serilog dedies a `Foundry.Deploy`
- [x] Remplacer le service de log existant par un nouveau service Serilog
- [x] Conserver `deployment-state.json` tel quel
- [x] Supprimer le flux de logs live UI (`DeploymentLogs` / `LogEmitted`)
- [x] Assurer la continuite bootstrap + exe sur `X:\Foundry\Logs\FoundryDeploy.log`
- **Status:** complete

### Phase 5: Validation
- [x] Build des deux projets
- [ ] Verifier ecriture fichier + sortie Debug VS
- [ ] Verifier retention Foundry (7 jours) et append Deploy (sans retention)
- [ ] Verifier hooks exceptions globales
- **Status:** in_progress

### Phase 6: Delivery
- [x] Verifier coherence du plan et des impacts
- [x] Livrer et valider avec l utilisateur
- **Status:** complete

## Key Questions
1. Quel format de timestamp utiliser ? -> UTC ISO-8601.
2. Quels sinks utiliser ? -> Fichier + Debug VS (pas de sink Console).
3. Comment gerer la retention ? -> Foundry: 7 jours; Foundry.Deploy: aucune retention.
4. Quel nom/fichier de continuite bootstrap + exe ? -> `X:\Foundry\Logs\FoundryDeploy.log`.

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Implementations Serilog separees par projet (pas de code commun) | Respect strict du besoin utilisateur |
| Niveau global `Verbose` sur les 2 apps | Couvrir tous les niveaux de logs |
| Format timestamp UTC ISO-8601 | Uniforme, robuste et coherent multi-environnement |
| Sinks: `File` + `Debug` uniquement | Persistant + visible dans Output VS |
| `Foundry`: fichier par session + retention 7 jours + sans limite de taille | Lecture simple par lancement, nettoyage automatique |
| `Foundry.Deploy`: fichier unique `FoundryDeploy.log`, append, sans retention | Continuite bootstrap PowerShell -> exe en WinPE |
| Conserver `deployment-state.json` identique | Eviter regression fonctionnelle de l orchestration |
| Supprimer logs live UI `DeploymentLogs` dans Deploy | Alignement avec choix utilisateur |
| Hooks exceptions globales sur les 2 apps | Capturer les erreurs non gerees |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Build error CS0103 (`Path/Directory/File`) dans nouveaux fichiers logging Deploy | 1 | Ajout explicite de `using System.IO;` dans les fichiers concernés |

## Notes
- Le plan est pret pour implementation.
- Le nom de fichier de log Deploy est fige: `X:\Foundry\Logs\FoundryDeploy.log`.
- La migration remplace la logique de logging existante par Serilog, tout en conservant la persistence d etat Deploy.

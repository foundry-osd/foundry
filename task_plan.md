# Task Plan: Foundry Recovery V1 (UEFI/GPT) - Plan uniquement

## Goal
Definir un plan decision-complete pour ajouter dans `Foundry.Deploy` une partition de recuperation pleinement fonctionnelle en V1, en combinant les bonnes bases OSDCloud et les bonnes pratiques Microsoft, sans implementer le code dans cette etape.

## Current Phase
Phase 2

## Phases
### Phase 1: Cadrage et decisions
- [x] Valider les attentes produit et les choix de comportement V1
- [x] Verifier l'implementation actuelle OSDCloud et Foundry
- [x] Figer les decisions de perimetre
- **Status:** complete

### Phase 2: Plan technique detaille
- [x] Definir les changements d'API internes (services et modeles)
- [x] Definir la sequence d'orchestration cible (steps)
- [x] Definir la logique disque + WinRE + fermeture Recovery
- **Status:** complete

### Phase 3: Preparation implementation
- [ ] Lister fichiers a modifier et ordre d'execution
- [ ] Lister risques techniques et mitigations
- [ ] Preparer checklist de livraison
- **Status:** pending

### Phase 4: Handover
- [ ] Produire un plan final executable par un implementeur
- [ ] Confirmer explicitement qu'aucune implementation n'a ete faite
- **Status:** pending

## Key Questions
1. Quelles modifications minimales d'architecture sont necessaires pour supporter Recovery + WinRE proprement ?
2. Comment garantir un comportement robuste en cas d'echec `reagentc` ?
3. Comment conserver le mode dry-run coherent avec le nouveau workflow Recovery ?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Creer Recovery systematiquement en V1 | Exigence user: disque cible complet avec partition de recuperation |
| Perimetre firmware: UEFI/GPT uniquement | Aligne l'etat actuel de Foundry et reduit le risque |
| Pas de toggle UI Recovery en V1 | Simplicite et predictibilite du comportement |
| Taille Recovery: 990MB | Choix user base sur bonne pratique Microsoft |
| Partition Recovery cachee en fin de workflow | Eviter exposition d'une partition systeme a l'utilisateur |
| Direction: Microsoft best-practice (pas strict OSDCloud) | Recovery doit etre fonctionnelle et verifiable en V1 |
| Echec bloquant si chaine Recovery/WinRE invalide | Exigence de robustesse fonctionnelle des la V1 |
| Pas d'implementation dans cette etape | Demande explicite: plan uniquement |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `python` introuvable pour `session-catchup.py` | 1 | Continuer manuellement et journaliser dans progress.md |
| `py` introuvable pour `session-catchup.py` | 2 | Continuer manuellement et demarrer un nouveau cycle de planification |

## Notes
- Changement cible principal: `WindowsDeploymentService` + `DeploymentOrchestrator`.
- `MainWindow` et `MainWindowViewModel` restent sans nouvelle option Recovery en V1.
- Les modifications code sont planifiees mais non executees a ce stade.

## Implementation Plan (No Execution)
### Scope
- Ajouter Recovery V1 fonctionnelle avec layout UEFI/GPT complet: EFI + MSR + Windows + Recovery.
- Configurer WinRE explicitement apres application image.
- Cacher la partition Recovery en fin de workflow.
- Echouer le deploiement si Recovery/WinRE n'est pas conforme.

### Planned Code Changes
1. `src/Foundry.Deploy/Services/Deployment/DeploymentTargetLayout.cs`
- Ajouter:
  - `RecoveryPartitionRoot` (string)
  - `RecoveryPartitionLetter` (char)

2. `src/Foundry.Deploy/Services/Deployment/IWindowsDeploymentService.cs`
- Ajouter:
  - `Task ConfigureRecoveryEnvironmentAsync(...)`
  - `Task SealRecoveryPartitionAsync(...)`

3. `src/Foundry.Deploy/Services/Deployment/DeploymentRuntimeState.cs`
- Ajouter:
  - `TargetRecoveryPartitionRoot` (string?)
  - `WinReConfigured` (bool)
  - `WinReInfoOutputPath` (string?)

4. `src/Foundry.Deploy/Services/Deployment/WindowsDeploymentService.cs`
- Mettre a jour `PrepareTargetDiskAsync`:
  - `clean`, `convert gpt`, EFI 260MB, MSR 16MB, Windows primaire.
  - `shrink minimum=990 desired=990`.
  - creation Recovery 990MB, format NTFS, GUID recovery `de94...`, attribut GPT `0x8000000000000001`.
  - lettre temporaire Recovery (priorite `R`, fallback lettre libre).
- Ajouter `ConfigureRecoveryEnvironmentAsync`:
  - copier `winre.wim` de Windows offline vers `Recovery\\WindowsRE`.
  - executer `reagentc /setreimage`, `reagentc /enable`, `reagentc /info` avec `/target`.
  - valider `Enabled` et chemin Recovery.
- Ajouter `SealRecoveryPartitionAsync`:
  - retirer la lettre Recovery temporaire.
  - verifier non-exposition de lettre.

5. `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs`
- Inserer une etape explicite:
  - `Configure recovery environment` entre `Apply operating system image` et `Apply offline drivers`.
- Appeler:
  - `ConfigureBootAsync` (deja present),
  - `ConfigureRecoveryEnvironmentAsync`,
  - `SealRecoveryPartitionAsync`.
- Stocker infos Recovery dans `runtimeState`.
- Maintenir un dry-run coherent avec la nouvelle etape.

### Planned Workflow Order
1. Initialize deployment workspace
2. Validate target configuration
3. Resolve cache strategy
4. Prepare target disk layout
5. Download operating system image
6. Download and prepare driver pack
7. Apply operating system image
8. Configure recovery environment
9. Apply offline drivers
10. Execute full Autopilot workflow
11. Finalize deployment and write logs

### Planned Validation and Tests
1. Unit tests
- Generation script diskpart contient `shrink` + GUID/attributs Recovery.
- Parsing/validation sortie `reagentc /info`.
- Non-regression `ResolveImageIndexAsync`.

2. Integration tests (mock `IProcessRunner`)
- Sequence commandes attendue: diskpart -> dism apply -> bcdboot -> reagentc -> remove recovery letter.
- Echec bloquant si `reagentc` invalide.

3. E2E acceptance criteria
- Layout final contient EFI/MSR/Windows/Recovery.
- Recovery sans lettre visible.
- WinRE `Enabled` et associe a la partition Recovery.
- Echec explicite si chaine Recovery/WinRE non valide.

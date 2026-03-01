# Task Plan: Analyse erreurs déploiement OS et comparaison OSDCloud

## Goal
Identifier la cause des erreurs visibles dans `FoundryDeploy.log`, vérifier si elles sont réellement gérées dans le code local, puis comparer ce comportement à celui d'OSDCloud.

## Current Phase
Phase 6

## Phases

### Phase 1: Requirements & Discovery
- [x] Comprendre la demande utilisateur
- [x] Identifier les fichiers et sources à analyser
- [x] Documenter les premiers constats dans `findings.md`
- **Status:** complete

### Phase 2: Analyse locale
- [x] Relever les erreurs exactes dans le log
- [x] Relier chaque erreur au code local
- [x] Évaluer si le comportement est toléré, dégradé, ou bloquant
- **Status:** complete

### Phase 3: Comparaison OSDCloud
- [x] Identifier les flux équivalents dans OSDCloud
- [x] Vérifier la gestion des mêmes cas
- [x] Documenter les différences de robustesse
- **Status:** complete

### Phase 4: Correctifs de code
- [x] Supprimer le `shrink` et dimensionner directement la partition Windows
- [x] Rendre la configuration WinRE tolérante à l'absence de `reagentc.exe`
- [x] Simplifier le flux si nécessaire
- **Status:** complete

### Phase 5: Synthèse
- [x] Formuler la cause racine
- [x] Distinguer warning vs erreur bloquante
- [x] Préparer une réponse exploitable pour l'utilisateur
- **Status:** complete

### Phase 6: Delivery
- [x] Vérifier les références
- [x] Livrer la synthèse
- **Status:** in_progress

## Key Questions
1. Pourquoi `DISM /Get-CurrentEdition` retourne-t-il `1639` après l'application de l'image ?
2. Pourquoi `reagentc.exe` est-il introuvable dans ce contexte WinPE, et comment OSDCloud évite ou contourne-t-il cela ?

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| Analyser d'abord le log et `WindowsDeploymentService` | Ce sont les sources directes des erreurs visibles |
| Vérifier OSDCloud sur GitHub ensuite | La comparaison doit se faire sur des flux réels et actuels |
| Vérifier `ProcessRunner` avant de conclure sur DISM | Le code utilise deux modes d'invocation différents (`Arguments` vs `ArgumentList`) qui changent le parsing Windows |
| Corriger uniquement `GetAppliedWindowsEditionAsync` | C'est l'appel DISM qui a généré le `1639` observé dans le log |
| Passer la taille disque validée à `PrepareTargetDiskAsync` | Cela évite de redétecter le disque dans le service Windows et permet de supprimer le `shrink` proprement |
| Traiter `reagentc` comme best-effort | Le staging de `winre.wim` doit rester possible même si WinPE n'embarque pas `reagentc.exe` |
| Supprimer finalement tout usage de `reagentc` | L'utilisateur veut un alignement OSDCloud complet sur le traitement de WinRE |
| Revenir à `reagentc` avec la doc Microsoft | L'utilisateur veut que la partition Recovery porte réellement WinRE et soit activée correctement |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Commande PowerShell composée refusée par la politique shell | 1 | Remplacée par des commandes `git` simples séparées |
| Build cassé après changement de signature `ResolveTargetOsGuidAsync` | 1 | Appel mis à jour, rebuild vert |

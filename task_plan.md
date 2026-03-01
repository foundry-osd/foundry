# Task Plan: Analyse erreurs déploiement OS et comparaison OSDCloud

## Goal
Identifier la cause des erreurs visibles dans `FoundryDeploy.log`, vérifier si elles sont réellement gérées dans le code local, puis comparer ce comportement à celui d'OSDCloud.

## Current Phase
Phase 5

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

### Phase 4: Synthèse
- [x] Formuler la cause racine
- [x] Distinguer warning vs erreur bloquante
- [x] Préparer une réponse exploitable pour l'utilisateur
- **Status:** complete

### Phase 5: Delivery
- [x] Vérifier les références
- [ ] Livrer la synthèse
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

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| Commande PowerShell composée refusée par la politique shell | 1 | Remplacée par des commandes `git` simples séparées |

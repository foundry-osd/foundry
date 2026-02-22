# Foundry.Deploy - Plan d'implementation

## Etat implementation actuel (2026-02-22)

- Projet `Foundry.Deploy` cree et ajoute a la solution.
- Wizard + page progress en place (squelette fonctionnel).
- Chargement des catalogues OS/DriverPack depuis `Foundry.Automation` implemente.
- Orchestrateur de sequence en place (squelette d'etapes, y compris etape Autopilot full).
- Bootstrap WinPE `FoundryBootstrap.ps1` migre vers telechargement GitHub Release `latest` via BITS.
- Build solution valide en ARM64 et x64.

## 1. Constat de l'existant

### Foundry (actuel)
- Architecture WPF + DI deja en place, orientee services (`Program.cs` enregistre `IOperationProgressService`, `IWinPeBuildService`, `IMediaOutputService`).
- Theme Fluent deja centralise (`App.xaml`, `ThemeService`).
- Injection bootstrap WinPE deja presente lors du build media:
  - script copie dans `X:\Windows\System32\FoundryBootstrap.ps1`
  - invocation ajoutee dans `startnet.cmd`.
- Mode USB actuel cree deja une partition cache dediee `Foundry Cache` (BOOT FAT32 + cache NTFS).

### OSDCloud (reference)
- Orchestrateur en mode "task sequence" (workflow JSON + etapes executees dynamiquement).
- Separation nette:
  - couche UI (selection + lancement)
  - couche execution (steps 1..9).
- Strategie cache/log robuste:
  - cache local `C:\OSDCloud\...`
  - recherche offline sur tous les volumes `*\OSDCloud\OS` et `*\OSDCloud\DriverPacks`
  - logs consolides dans `C:\Windows\Temp\osdcloud-logs`
  - verification hash et telechargement resumable.

### Catalogues Foundry.Automation (valide 2026-02-22)
- OS: `OperatingSystemCatalog` (`Items/Item`, 3037 entrees)
- DriverPack: `DriverPackCatalog` (`DriverPacks/DriverPack`, 8503 entrees)
- Ces schemas sont suffisants pour un moteur de selection filtre (arch, release, langue, edition, constructeur, modele).

## 2. Objectif cible Foundry.Deploy

Creer `Foundry.Deploy` comme **orchestrateur de sequence de deploiement OS** en WinPE, avec:
- Wizard de configuration
- Page de progression temps reel (etapes, statut, logs, erreurs)
- Gestion cache differenciee USB vs ISO
- Telechargement/installation OS + driverpack + **Autopilot complet**.

## 3. Architecture proposee (alignee Foundry)

### Solution
- Ajouter un second projet:
  - `src/Foundry.Deploy/Foundry.Deploy.csproj`
- Ajouter le projet a `src/Foundry.slnx`.

### Couches du projet
- `Foundry.Deploy`
  - `Views/`  
    `MainWindow.xaml`, `WizardPage*.xaml`, `DeploymentProgressPage.xaml`
  - `ViewModels/`  
    `MainWindowViewModel`, `DeploymentWizardViewModel`, `DeploymentProgressViewModel`
  - `Models/`  
    `DeploymentContext`, `CatalogSelection`, `DeploymentStepResult`, `CacheLayout`
  - `Services/`
    - `Catalog/`
      - `IOperatingSystemCatalogService`
      - `IDriverPackCatalogService`
      - `OperatingSystemCatalogService` (parse `OperatingSystem.xml`)
      - `DriverPackCatalogService` (parse `DriverPack_Unified.xml`)
    - `Deployment/`
      - `IDeploymentOrchestrator`
      - `DeploymentOrchestrator`
      - `IDeploymentStep`
      - `DeploymentStepBase`
      - `Steps/` (Initialize, Validate, Disk, Download, ApplyImage, Drivers, Autopilot, Finalize)
    - `Cache/`
      - `ICacheStrategy`
      - `UsbCacheStrategy`
      - `IsoCacheStrategy`
      - `CacheLocatorService`
    - `Logging/`
      - `IDeploymentLogService`
      - `DeploymentLogService`
    - `System/`
      - wrappers process/DISM/BITS/WMI (meme style que `WinPeProcessRunner`)
  - `Resources/`  
    `AppStrings.resx`, `AppStrings.fr-FR.resx`, `AppStrings.en-US.resx`

### Principes d'orchestration
- Pattern OSDCloud a reproduire:
  - **declaratif**: sequence d'etapes definie par metadonnees
  - **imperatif**: chaque etape implemente son execution.
- Proposition .NET:
  - `DeploymentWorkflowDefinition` (liste ordonnee d'etapes + conditions skip)
  - `IDeploymentStep.ExecuteAsync(DeploymentContext ctx, CancellationToken ct)`
  - sortie standardisee: `DeploymentStepResult` (Success/Warning/Failure + code + details).

## 4. UX cible (wizard + progress)

### Wizard (etapes)
1. `Welcome + Mode Detection`
2. `Operating System Selection` (release, edition, langue, architecture)
3. `Driver Strategy` (auto detect modele + choix pack)
4. `Disk / Target Validation` (disque cible + confirmation destructive)
5. `Summary` (recap + Start)

### Progress page
- Timeline verticale d'etapes (Pending / Running / Completed / Failed / Skipped)
- Barre progression globale + progression etape
- Console logs en temps reel (filtrable Info/Warning/Error)
- Boutons: `Cancel`, `Open Logs`, `Retry failed step` (v2), `Shell` (optionnel).

## 5. Strategie cache USB vs ISO

### Conventions dossier proposees
- Racine runtime locale (toujours disponible):
  - `X:\Windows\Temp\Foundry\Deploy\` (runtime)
- Racine persistante (si disponible):
  - USB mode: `<CacheDrive>:\Foundry Cache\Deploy\`
  - ISO mode: fallback `C:\Foundry\Deploy\` (ephemere ou disk interne selon scenario)

Sous-dossiers standards:
- `Catalogs\` (copies XML + metadata generation time)
- `OS\` (images ESD/WIM telechargees)
- `DriverPacks\` (archives OEM)
- `Extracted\Drivers\` (payload INF)
- `Logs\` (transcript, dism, etapes)
- `State\` (`deployment-state.json`, resume)
- `Temp\` (scratch)

### Regles mode USB
- Priorite lecture/ecriture sur `Foundry Cache`.
- Support offline-first:
  - si fichier existe + hash OK => reuse
  - sinon telechargement puis persistance cache.

### Regles mode ISO
- Pas de partition cache dediee.
- Utiliser `C:\Foundry\Deploy\` + nettoyage agressif post-deploiement configurable.
- Si volume USB data detecte (optionnel), permettre redirection cache utilisateur.

## 6. Bootstrap WinPE (FoundryBootstrap.ps1)

### Contrat cible
Au boot WinPE:
1. Detecter architecture (`AMD64`/`ARM64`)
2. Resoudre release cible **`latest` permanent**
3. Telecharger via BITS un zip release:
   - exemple: `Foundry.Deploy-win-x64.zip`
4. Verifier hash (sha256) via manifest release
5. Extraire vers `X:\ProgramData\Foundry\Deploy\current\`
6. Executer `Foundry.Deploy.exe`
7. Logger dans `X:\Windows\Temp\FoundryBootstrap.log`.

### Artifacts release recommandes
- `Foundry.Deploy-win-x64.zip`
- `Foundry.Deploy-win-arm64.zip`
- `foundry-deploy-manifest.json` avec:
  - version
  - url per arch
  - sha256
  - publishedAt.

## 7. Publication .NET 10 WPF

Parametres cibles `Foundry.Deploy.csproj`:
- `TargetFramework=net10.0-windows`
- `UseWPF=true`
- `RuntimeIdentifiers=win-x64;win-arm64`
- Publish:
  - `PublishSingleFile=true`
  - `SelfContained=true`
  - `IncludeNativeLibrariesForSelfExtract=true`
  - `DebugType=none` pour artifact release.

## 8. Roadmap implementation

### Milestone 1 - Skeleton projet
- Creer `Foundry.Deploy.csproj`
- Reprendre DI, ThemeService, LocalizationService pattern Foundry
- MainWindow + navigation wizard skeleton

### Milestone 2 - Catalog + selection UX
- Parse XML OS/DriverPack
- Filtrage (arch, langue, edition, constructeur/modele)
- Wizard selection + summary valide

### Milestone 3 - Orchestrateur + etapes core
- Step engine + definitions
- Etapes:
  - initialize/log
  - validate target
  - download OS
  - apply image
  - driverpack download/extract/apply
  - finalize/bcdboot
- Progress page live + cancellation

### Milestone 4 - Cache strategy + resilience
- UsbCacheStrategy + IsoCacheStrategy
- Hash validation + resume downloads (BITS/curl fallback)
- Structured logs + state persistence

### Milestone 5 - Bootstrap + release integration
- Implementer `FoundryBootstrap.ps1` complet
- Pipeline release zip + manifest
- Test WinPE end-to-end USB + ISO

## 9. Risques et garde-fous

- WinPE networking instable:
  - timeout, retry exponentiel, mode offline fallback.
- Volumetrie cache ISO:
  - nettoyage automatique parametre + warning UI.
- Heterogeneite driverpack OEM:
  - extraction strategy par format + detection echec explicite.
- Operations destructives disque:
  - double confirmation + validation identite disque (modele/serial/uniqueId).

## 10. Questions ouvertes (a valider)

Toutes les questions initiales sont maintenant tranchees:
- V1: **Autopilot complet**
- ISO mode: cache local autorise dans **`C:\Foundry\Deploy`**
- Bootstrap: canal **`latest` permanent**
- Catalogue OS: **toutes editions/langues exposees**
- Telemetrie: **zero telemetrie** (aucune emission)

## 11. Decisions verrouillees (2026-02-22)

- Aucun mecanisme de telemetrie n'est autorise dans Foundry.Deploy.
- Le wizard doit afficher l'integralite des editions/langues presentes dans `OperatingSystem.xml`.
- Le pipeline Autopilot est en scope v1, pas seulement des hooks.
- Le bootstrap WinPE telecharge toujours le dernier artifact release (`latest`) de `mchave3/Foundry`.

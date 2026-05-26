# Foundry Localization Resources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Review and normalize localization resources for Foundry OSD, Foundry Connect, and Foundry Deploy, using `en-US` as the source of truth.

**Architecture:** Keep the resource file structure unchanged. First stabilize the source English copy, then align `fr-FR`, then add ADK-backed culture folders by cloning the reviewed `en-US` key sets so every supported WinPE language has complete resource coverage.

**Tech Stack:** .NET 10, WinUI 3 `.resw` resources for Foundry OSD, WPF `.resx` resources for Foundry Connect and Foundry Deploy, Windows ADK WinPE optional component language folders.

---

### Task 1: Baseline Inventory And Guardrails

**Files:**
- Read: `src/Foundry/Strings/en-US/Resources.resw`
- Read: `src/Foundry.Connect/Strings/en-US/Resources.resx`
- Read: `src/Foundry.Deploy/Strings/en-US/Resources.resx`
- Create or modify only if needed: focused resource validation tests under existing test projects

- [x] **Step 1: Create isolated worktree**

Worktree:

```powershell
E:\Github\Foundry Project\foundry-worktrees\localization-resources
```

Branch:

```powershell
feat/foundry-localization-resources
```

- [x] **Step 2: Verify baseline tests**

Run:

```powershell
dotnet test Foundry.slnx --configuration Release
```

Result:

```text
573 tests passed, 0 failed
```

- [x] **Step 3: Count current source resources**

Current resource counts:

```text
Foundry OSD en-US:      626 keys
Foundry OSD fr-FR:      626 keys
Foundry Connect en-US:   98 keys
Foundry Connect fr-FR:   98 keys
Foundry Deploy en-US:   389 keys
Foundry Deploy fr-FR:   389 keys
```

- [x] **Step 4: Confirm WinPE language list behavior**

The General page already binds the WinPE language picker to dynamic ADK discovery through:

```text
src/Foundry/ViewModels/GeneralConfigurationViewModel.cs
src/Foundry/Views/GeneralConfigurationPage.xaml
src/Foundry.Core/Services/WinPe/WinPeLanguageDiscoveryService.cs
```

No hardcoded language list was found for the General page.

### Task 2: Review And Patch en-US Source Copy

**Files:**
- Modify: `src/Foundry/Strings/en-US/Resources.resw`
- Modify: `src/Foundry.Connect/Strings/en-US/Resources.resx`
- Modify: `src/Foundry.Deploy/Strings/en-US/Resources.resx`
- Modify tests only when exact text expectations change

- [x] **Step 1: Review Foundry OSD en-US copy**

Check the 626 keys for wording consistency, especially:

```text
AboutDialog.ContributionCountFormat
Autopilot.TenantPickerSelectedCountFormat
Documentation.ExternalLaunchFailed.Title
StartMedia.FinalExecution.*
StartMedia.BlockingReason.FinalExecutionDeferred
StartMedia.Operation.Downloading
StartMedia.Operation.DownloadingDriverPackage
certificate is expired
```

- [x] **Step 2: Review Foundry Connect en-US copy**

Check the 98 keys for wording consistency, especially:

```text
Tools.RefreshStatus
About.Description
About.License
About.Contributors
Ethernet.NoAdapterDetected
```

- [x] **Step 3: Review Foundry Deploy en-US copy**

Check the 389 keys for wording consistency, especially:

```text
Preparation.DryRunDescription
Preparation.FirmwareUpdateEnabled
Preparation.NetworkShareRequired*
OS image / operating system image
```

- [x] **Step 4: Patch source English only**

Use `en-US` as the only source edited in this task. Keep keys stable unless a key name is clearly wrong and all call sites can be updated safely.

- [x] **Step 5: Run focused tests**

Run:

```powershell
dotnet test Foundry.slnx --configuration Release
```

Expected:

```text
0 failed
```

- [x] **Step 6: Commit en-US pass**

Commit:

```powershell
git add src
git commit -m "fix(localization): refine en-US resource copy"
```

### Task 3: Rework fr-FR Against Reviewed en-US

**Files:**
- Modify: `src/Foundry/Strings/fr-FR/Resources.resw`
- Modify: `src/Foundry.Connect/Strings/fr-FR/Resources.resx`
- Modify: `src/Foundry.Deploy/Strings/fr-FR/Resources.resx`

- [ ] **Step 1: Define a small French glossary**

Use consistent product terminology:

```text
WinPE -> WinPE
Foundry OSD / Connect / Deploy -> unchanged
boot -> démarrage unless a Windows/ADK term requires boot
runtime -> environnement d'exécution or runtime, chosen consistently
hardware hash -> hash matériel
tenant -> tenant, unless the surrounding sentence reads better as locataire Microsoft Entra
Microsoft Update Catalog -> Microsoft Update Catalog
Retail / Volume -> keep Windows license channel terms unchanged unless existing UI requires translation
```

- [ ] **Step 2: Rework Foundry OSD fr-FR**

Align all 626 `fr-FR` values with the reviewed `en-US` source. Remove awkward plural shortcuts such as `profil(s)` where a clearer sentence is possible.

- [ ] **Step 3: Rework Foundry Connect fr-FR**

Align all 98 `fr-FR` values with the reviewed `en-US` source. Standardize Wi-Fi and network security terms.

- [ ] **Step 4: Rework Foundry Deploy fr-FR**

Align all 389 `fr-FR` values with the reviewed `en-US` source. Standardize deployment, dry-run, firmware, and catalog terminology.

- [ ] **Step 5: Verify key and placeholder parity**

Compare each `fr-FR` file against its matching `en-US` file:

```powershell
dotnet test Foundry.slnx --configuration Release
```

Expected:

```text
0 failed
```

- [ ] **Step 6: Commit fr-FR pass**

Commit:

```powershell
git add src
git commit -m "fix(localization): align fr-FR resources"
```

### Task 4: Add ADK Language Resource Files

**Files:**
- Create: `src/Foundry/Strings/<culture>/Resources.resw`
- Create: `src/Foundry.Connect/Strings/<culture>/Resources.resx`
- Create: `src/Foundry.Deploy/Strings/<culture>/Resources.resx`
- Modify if supported UI languages are intended to expand: `src/Foundry.Localization/FoundrySupportedCultures.cs`

- [ ] **Step 1: Use the installed ADK language folders**

The current amd64 ADK folders are:

```text
ar-SA, bg-BG, cs-CZ, da-DK, de-DE, el-GR, en-GB, en-US, es-ES, es-MX,
et-EE, fi-FI, fr-CA, fr-FR, he-IL, hr-HR, hu-HU, it-IT, ja-JP, ko-KR,
lt-LT, lv-LV, nb-NO, nl-NL, pl-PL, pt-BR, pt-PT, ro-RO, ru-RU, sk-SK,
sl-SI, sr-Latn-RS, sv-SE, th-TH, tr-TR, uk-UA, zh-CN, zh-TW
```

- [ ] **Step 2: Generate missing resource folders**

For each ADK culture missing from an app, create the matching file from the reviewed `en-US` file:

```text
src/Foundry/Strings/<culture>/Resources.resw
src/Foundry.Connect/Strings/<culture>/Resources.resx
src/Foundry.Deploy/Strings/<culture>/Resources.resx
```

Initial values should match reviewed `en-US` unless a verified translation is intentionally supplied in the same task.

- [ ] **Step 3: Preserve XML structure**

Keep each generated file valid XML and preserve the existing `data` key ordering, comments, value placeholders, and `xml:space="preserve"` attributes.

- [ ] **Step 4: Verify project inclusion**

Confirm existing project globs include the generated resources:

```text
src/Foundry/Foundry.csproj
src/Foundry.Connect/Foundry.Connect.csproj
src/Foundry.Deploy/Foundry.Deploy.csproj
```

- [ ] **Step 5: Commit generated languages**

Commit:

```powershell
git add src
git commit -m "feat(localization): add ADK language resource files"
```

### Task 5: Verification And Pull Request

**Files:**
- Review all modified resource files
- Update PR metadata only after final verification

- [ ] **Step 1: Run full test suite**

Run:

```powershell
dotnet test Foundry.slnx --configuration Release
```

Expected:

```text
0 failed
```

- [ ] **Step 2: Check git diff**

Run:

```powershell
git status --short
git diff --stat
```

Expected:

```text
Only localization resources, focused tests if added, and this plan file changed.
```

- [ ] **Step 3: Push branch**

Run:

```powershell
git push -u origin feat/foundry-localization-resources
```

- [ ] **Step 4: Open one PR without squash**

PR title:

```text
feat(localization): expand Foundry language resources
```

PR description must include:

```text
Summary
Reason
Main changes
Testing notes
```

Do not squash commits.

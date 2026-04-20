# Task Plan

## Goal
Uniformize language handling across the repository while keeping each project independent:
- UI culture selection in `Foundry`, `Foundry.Deploy`, and `Foundry.Connect`.
- Deployable Windows language registry in `Foundry`.
- Expert configuration language fields passed from `Foundry` to `Foundry.Deploy`.
- Operating system catalog language filters in `Foundry.Deploy`.
- WinPE language selection and display in `Foundry`.

## Current Branch
- Branch: `codex/audit-language-lists`
- Base: `main`
- Scope: language cleanup only

## Constraints
- Do not introduce shared services or shared models between projects.
- Keep UI cultures, deployable Windows languages, OS catalog languages, and WinPE languages conceptually separate.
- Treat `en-US` and `fr-FR` as initial data, not structural assumptions.
- Prefer data-driven project-local catalogs over hardcoded XAML menu items.
- Prefer logic/unit tests over UI tests.
- Keep `.resx` resources as the UI localization mechanism.
- Do not redesign `.resx` files unless a concrete inconsistency is found.

## Implementation Status
1. Inspect current UI culture command and XAML binding patterns. - complete
2. Add per-project supported UI culture options. - complete
3. Replace hardcoded language menu items with bound menu generation. - complete
4. Remove obsolete UI culture converter usage. - complete
5. Add canonical language code utilities where language codes cross configuration boundaries. - complete
6. Tighten deployable language registry validation tests. - complete
7. Tighten expert config and OS catalog language tests. - complete
8. Tighten WinPE language canonicalization tests. - complete
9. Rebuild and run all tests. - complete
10. Prepare handoff planning files for another workstation. - complete

## Design Decisions
- Each project owns its own supported UI culture catalog:
  - `src/Foundry/ViewModels/SupportedCultureCatalog.cs`
  - `src/Foundry.Deploy/ViewModels/SupportedCultureCatalog.cs`
  - `src/Foundry.Connect/ViewModels/SupportedCultureCatalog.cs`
- Each catalog currently contains `en-US` and `fr-FR`, but the code supports more entries without XAML changes.
- UI language menu labels are still resource-backed through `Language.*` keys.
- Deployment languages stay separate from UI cultures.
- WinPE languages stay dynamic because they depend on ADK language packs.
- Canonical language codes are written/displayed as `CultureInfo.Name` when .NET recognizes the culture.
- Comparison remains tolerant of casing and `_` vs `-`.

## Changed Areas
- UI culture menus:
  - Removed hardcoded `en-US` / `fr-FR` menu items from the three main windows.
  - Menus now bind to `SupportedCultures`.
- UI culture catalogs:
  - Added local `SupportedCultureCatalog` and `SupportedCultureOption` files to each app.
- Resource metadata:
  - Added `NeutralLanguage` set to `en-US` in the three WPF project files.
- Obsolete cleanup:
  - Removed `Foundry.Converters.CultureToBooleanConverter`.
  - Removed related XAML resource declarations.
- Foundry deployable language registry:
  - `EmbeddedLanguageRegistryService` now canonicalizes codes, trims names, and rejects duplicate language codes.
- Foundry expert/deploy config export:
  - `DeployConfigurationGenerator` now canonicalizes and de-duplicates `VisibleLanguageCodes`.
  - `DefaultLanguageCodeOverride` is canonicalized and cleared when not included in the visible language list.
- Foundry localization settings view model:
  - Applies and exports language codes through canonical comparison rules.
- Foundry.Deploy OS catalog filters:
  - Catalog language filter values are canonicalized for display.
  - Expert localization inputs still accept tolerant formats like `fr_fr`, `fr-fr`, and `FR-fr`.
- WinPE:
  - Added `WinPeLanguageUtility.Canonicalize`.
  - WinPE language display options now expose canonical codes when possible.

## Validation Completed
- `dotnet build src/Foundry/Foundry.csproj`
- `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj`
- `dotnet build src/Foundry.Connect/Foundry.Connect.csproj`
- `dotnet test src/Foundry.Tests/Foundry.Tests.csproj`
- `dotnet test src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj`
- `dotnet test src/Foundry.Connect.Tests/Foundry.Connect.Tests.csproj`
- `git diff --check`

## Test Result Summary
- `Foundry.Tests`: 19 passed
- `Foundry.Deploy.Tests`: 29 passed
- `Foundry.Connect.Tests`: 7 passed
- Total: 55 passed

## Follow-Up Notes
- Adding a new UI language now requires:
  - Add localized `.resx` resources for the project.
  - Add one entry to that project's `SupportedCultureCatalog`.
  - Add or update logic tests if the catalog rules change.
- Adding a new deployable Windows language requires:
  - Add one entry to `src/Foundry/Assets/Configuration/languages.json`.
  - Ensure the code is canonical and unique.
  - Existing registry tests should catch duplicates and malformed known cultures.
- WinPE language support should not be manually listed. It should continue to come from ADK language pack discovery.

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| `python` not available | Ran planning session catch-up | Continued without catch-up |
| `py` not available | Retried planning session catch-up | Continued without catch-up |
| PowerShell `Select-Object -Index 45..65` parsing error | Tried to read project file snippets | Switched to full file reads / valid indexing |
| Grep included deleted converter file from Git index | Checked obsolete references after deletion | Reran search only against paths that still exist |

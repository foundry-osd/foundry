# Findings

## Context7 / .NET Localization Guidance
- `ResourceManager` uses `CultureInfo.CurrentUICulture` for desktop resource lookup.
- Resource lookup falls back through culture-specific resources to neutral/default resources.
- The recommended .NET pattern is neutral resources in the main assembly and one satellite assembly per additional culture.
- `NeutralResourcesLanguageAttribute` / project `NeutralLanguage` helps define the fallback culture.

## Language Concepts In This Repository
- UI cultures: application interface language, currently `en-US` and `fr-FR`.
- Deployable Windows languages: available Windows deployment choices configured by Foundry.
- Expert config languages: `visibleLanguageCodes`, `defaultLanguageCodeOverride`, and `forceSingleVisibleLanguage`.
- OS catalog filter languages: language values derived dynamically from the remote OS catalog.
- WinPE languages: detected dynamically from ADK language pack folders.

## Important Separation
- UI cultures are not the same thing as deployable Windows languages.
- Deployable Windows languages are not the same thing as WinPE ADK language packs.
- `Foundry.Deploy` should not own a static OS language registry because its available languages come from the catalog and expert configuration.
- WinPE languages should remain dynamic because the installed ADK controls what is available.

## Initial Issues Found
- The UI language menu was hardcoded three times in XAML.
- `Foundry` had a broader deployable Windows language registry, separate from UI localization.
- `Foundry.Deploy` normalized language filters to lowercase internally, which was correct for comparison but not ideal for display/config cleanliness.
- `CultureToBooleanConverter` became obsolete after replacing hardcoded XAML menu items.
- Existing tests covered some deploy config, registry ordering, and WinPE normalization, but not the new UI culture catalogs.

## Implemented Decisions
- Added project-local supported UI culture catalogs in each app.
- Removed hardcoded language menu items.
- Kept menu labels resource-backed via `.resx`.
- Added `NeutralLanguage` metadata to the WPF project files.
- Removed `CultureToBooleanConverter`.
- Added canonical language code utilities at configuration/catalog boundaries.
- Made Foundry export canonical, de-duplicated language codes.
- Made Foundry.Deploy display canonical OS catalog language filter values while still accepting tolerant expert config input.
- Added WinPE language canonicalization while preserving dynamic discovery.

## Tests Added Or Updated
- Supported UI culture catalog tests in:
  - `src/Foundry.Tests/SupportedCultureCatalogTests.cs`
  - `src/Foundry.Deploy.Tests/SupportedCultureCatalogTests.cs`
  - `src/Foundry.Connect.Tests/SupportedCultureCatalogTests.cs`
- OS catalog language filter tests:
  - `src/Foundry.Deploy.Tests/OperatingSystemCatalogViewModelTests.cs`
- Deploy config language canonicalization:
  - `src/Foundry.Tests/DeployConfigurationGeneratorTests.cs`
- Embedded language registry validation:
  - `src/Foundry.Tests/EmbeddedLanguageRegistryServiceTests.cs`
- WinPE language canonicalization:
  - `src/Foundry.Tests/WinPeHelperTests.cs`

## Final Verification
- All three app projects build successfully.
- All three test projects pass.
- No stale hardcoded UI language menu command parameters remain.
- No active `CultureToBooleanConverter` references remain.
- `git diff --check` reports no whitespace errors.

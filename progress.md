# Progress

## Session Summary
- Created branch `codex/audit-language-lists`.
- Audited language handling across `Foundry`, `Foundry.Deploy`, and `Foundry.Connect`.
- Used Context7 for .NET localization guidance.
- Implemented language cleanup across UI cultures, deployable language registry, expert config export, OS catalog filtering, and WinPE language canonicalization.
- Added logic tests for the new language handling rules.
- Prepared this handoff for continuing work from another PC.

## Completed Work
- Added `task_plan.md`, `findings.md`, and `progress.md` for persistent context.
- Added supported UI culture catalogs per project.
- Added supported UI culture option models per project.
- Replaced hardcoded XAML language menu items with bound menu items.
- Removed `Foundry.Converters.CultureToBooleanConverter`.
- Added `NeutralLanguage` metadata to WPF project files.
- Added `LanguageCodeUtility` in Foundry configuration services.
- Added `LanguageCodeUtility` in Foundry.Deploy catalog services.
- Canonicalized deploy config language export.
- Canonicalized embedded deployable language registry entries.
- Canonicalized Foundry.Deploy OS catalog language filters.
- Added WinPE language canonicalization.
- Added or updated language logic tests.

## Commands Run
- `dotnet build src/Foundry/Foundry.csproj`
- `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj`
- `dotnet build src/Foundry.Connect/Foundry.Connect.csproj`
- `dotnet test src/Foundry.Tests/Foundry.Tests.csproj`
- `dotnet test src/Foundry.Deploy.Tests/Foundry.Deploy.Tests.csproj`
- `dotnet test src/Foundry.Connect.Tests/Foundry.Connect.Tests.csproj`
- `git diff --check`

## Latest Validation Results
- Builds: all passed with 0 errors.
- Tests: 55 passed.
- Whitespace check: passed.

## Current Git State Before Publish
- Branch: `codex/audit-language-lists`
- Intended commit scope: all current tracked and untracked files in this worktree.
- Planning files are intentionally included because work will continue from another PC.

## Next Publish Steps
1. Stage all changes.
2. Commit with a Conventional Commit message.
3. Push branch to `origin`.
4. Open a draft PR against `main`.

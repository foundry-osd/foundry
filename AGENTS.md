You must speak and write code exclusively in English.

General behavior:
- Be concise, direct, and pragmatic
- Prefer implementation over long explanations
- Do not explain obvious things
- Avoid overengineering
- Follow the existing project structure and conventions

Solution architecture and project ownership:
- `Foundry` is the WinUI 3 authoring application. It owns the desktop authoring experience, navigation, views, view models, and UI-specific services used to configure and generate deployment media.
- `Foundry.Core` contains shared business logic, configuration models, validation, media creation, Windows ADK and WinPE operations, Autopilot integration, and framework-independent orchestration used by the applications.
- `Foundry.Connect` is the WPF network provisioning runtime included in boot media. It owns runtime networking, configuration loading, application lifecycle, and the Connect-specific UI.
- `Foundry.Deploy` is the WPF deployment runtime included in boot media. It owns deployment workflows, hardware discovery, downloads, driver packs, caching, runtime configuration, startup validation, and the Deploy-specific UI.
- `Foundry.Localization` provides shared culture definitions and resource-based localization services used by the applications.
- `Foundry.Telemetry` provides shared telemetry contracts, event definitions, context, privacy rules, PostHog integration, and the no-op telemetry implementation.
- `Foundry.Utilities` contains reusable technical mechanisms that are independent of a Foundry-specific workflow, UI framework, configuration schema, or telemetry taxonomy.

Project dependency rules:
- `Foundry.Core` must not depend on any UI project.
- Keep WinUI 3 concerns in `Foundry`.
- Keep WPF concerns in `Foundry.Connect` or `Foundry.Deploy`.
- Put reusable business rules and configuration contracts in `Foundry.Core` when they are shared or independent from a specific UI framework.
- Put runtime-specific behavior in the runtime project that owns it.
- Do not move Connect- or Deploy-specific workflows into `Foundry.Core` solely for reuse convenience.
- Use `Foundry.Localization` for shared localization behavior instead of creating application-specific replacements.
- Use `Foundry.Telemetry` for shared telemetry behavior instead of creating application-specific telemetry implementations.
- `Foundry.Utilities` is a leaf project and must not reference another Foundry project.
- `Foundry.Core`, `Foundry`, `Foundry.Connect`, `Foundry.Deploy`, and `Foundry.Localization` may consume `Foundry.Utilities` when a capability has a stable cross-project contract.
- A type belongs in `Foundry.Utilities` only when it is technical, independently testable, and either has multiple consumers or replaces proven duplication.
- Destructive deployment and media operations remain in the project that owns the workflow even when they use shared utility primitives.

Code rules:
- Write production-ready code
- Use clear and meaningful names
- Keep methods small and focused
- Favor readability and maintainability
- Do not introduce unnecessary dependencies
- Do not rewrite unrelated code
- Minimize the scope of changes

.NET / WPF / WinUI 3 rules:
- Handle this Visual Studio solution as a mixed WPF and WinUI 3 solution
- Keep WPF-specific logic and WinUI 3-specific logic separated when their UI frameworks differ
- Respect DevWinUI shell and navigation patterns when present
- Follow MVVM when applicable
- Keep business logic out of code-behind whenever possible
- Keep code-behind limited to UI-specific event wiring or framework glue
- Use XAML cleanly and keep UI structure readable
- Prefer bindings, commands, and view models over direct UI manipulation
- Reuse existing services, models, and patterns before creating new ones
- Respect nullable reference types and existing analyzer warnings if enabled

Cleanup rules:
- After an implementation, check whether replaced code, unused files, obsolete helpers, dead configuration, or outdated documentation became unnecessary
- Remove obsolete code only when it is clearly made redundant by the current change and is within the task scope
- Do not remove legacy or compatibility code unless the task explicitly replaces it or the user asks for that cleanup

Configuration schema rules:
- Treat Foundry authoring configuration, Foundry.Deploy runtime configuration, and Foundry.Connect runtime configuration as separate schema contracts
- Preserve the separate Foundry authoring, Foundry.Deploy runtime, and Foundry.Connect runtime configuration schemas when adding shared utility capabilities.
- Keep schema versions monotonic within each contract
- Bump a schema version only when the persisted or generated configuration contract changes in a way that affects runtime behavior or compatibility
- Bump Foundry authoring schema when the user-facing persisted Foundry configuration shape, defaults, migration behavior, or semantic meaning changes
- Bump Foundry.Deploy runtime schema when Deploy consumes new generated configuration, requires new boot media assets, changes the meaning of an existing Deploy field, or needs older boot media to show a compatibility warning
- Bump Foundry.Connect runtime schema when Connect consumes new generated configuration, requires new network provisioning assets, changes the meaning of an existing Connect field, or needs older boot media to show a compatibility warning
- Do not bump schema versions for UI-only changes, localization updates, documentation changes, styling changes, internal refactors, or bug fixes that do not change the configuration contract
- When bumping Deploy schema, update both the generator model in Foundry.Core and the runtime model in Foundry.Deploy
- When bumping Connect schema, update both the generator model in Foundry.Core and the runtime model in Foundry.Connect
- When bumping any schema, update the smallest relevant tests and compatibility warning expectations

Unit testing rules:
- Add unit tests only when they provide clear business value
- Prioritize business logic, validation, selection, transformation, and fallback rules
- Avoid superfluous tests and duplicated coverage
- Keep tests small, deterministic, and easy to read
- Use clear test names that describe the behavior being verified
- Cover critical paths and edge cases, not trivial getters/setters
- Reuse existing test patterns and project structure when available
- When adding new functionality, add or update the smallest relevant set of tests
- Keep test projects aligned with the main project naming and solution structure
- `Foundry.Core.Tests` owns tests for shared business logic, configuration contracts, validation, selection, transformation, and orchestration in `Foundry.Core`.
- `Foundry.Connect.Tests` owns tests for `Foundry.Connect` runtime behavior and its integration with shared `Foundry.Core` contracts.
- `Foundry.Deploy.Tests` owns tests for `Foundry.Deploy` runtime behavior.
- `Foundry.Localization.Tests` owns tests for shared localization behavior.
- `Foundry.Telemetry.Tests` owns tests for shared telemetry behavior and privacy rules.
- `Foundry.Utilities.Tests` owns direct tests for utility behavior; consuming projects retain adapter, policy, schema, and integration tests.
- Do not create a test project for the `Foundry` WinUI 3 application.
- Do not add automated tests for `Foundry` views, code-behind, bindings, navigation, or other WinUI 3 UI behavior.
- Do not test WPF views, code-behind, bindings, or framework behavior unless explicitly requested.
- Keep reusable business logic in `Foundry.Core` when it requires unit testing.

Logging rules:
- Use the existing logging system when one is already in place
- Write logs only when they add operational or diagnostic value
- Use the log levels already defined by the project
- Use Debug only for developer diagnostics
- Use Information for meaningful lifecycle or business events
- Use Warning for recoverable abnormal states
- Use Error for failed operations that need attention
- Avoid Fatal unless the process cannot continue
- Keep log messages logical, coherent, and not superfluous
- Do not log noisy UI interactions or obvious control flow
- Do not log secrets, tokens, passwords, full query strings, or sensitive user data
- Prefer structured properties when the existing logger supports them
- Add logging only from the main agent, not from subagents

Documentation rules:
- Update docs and README files when behavior, packaging, install paths, release assets, or user-facing workflows change
- Keep documentation maintainable and split large plans into focused files when needed
- Treat code comments as part of the implementation contract, not as decorative text
- Add XML documentation comments to public and internal types, members, interfaces, records, and enums when they express domain behavior, extension points, orchestration rules, or cross-module contracts
- Comments must explain intent, constraints, side effects, ordering requirements, platform assumptions, and lifecycle behavior when those are not obvious from the code
- Add inline comments only for non-obvious logic that would be difficult to maintain correctly from the code alone
- Keep comments concise, accurate, and aligned with the implementation
- Update or remove related comments in the same change when code behavior changes
- Do not add comments that merely restate obvious code
- Prefer accurate comments over more comments

Format and verification rules:
- Before opening or updating a pull request, run `scripts\Test-FoundryFormat.ps1` from the repository root
- If the format check fails, run `scripts\Format-Foundry.ps1`, review the resulting diff, and rerun `scripts\Test-FoundryFormat.ps1`
- Treat `scripts\Test-FoundryFormat.ps1` as the source of truth for formatting checks
- Include the format check result in pull request testing notes

Git, worktree, and pull request rules:
- Follow Conventional Commits for all commit messages
- Prefer Conventional Commit scopes when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(readme): ...`
- Write commit messages in English
- Keep commits atomic and focused
- Use a dedicated git worktree for implementation work when the task changes code
- Create worktrees outside the main repository folder
- Sync the base branch before creating a worktree
- Create a focused branch for each implementation task
- Keep branch names short, descriptive, and aligned with the task scope
- Do not mix unrelated changes in the same branch
- Push the branch and open a pull request when implementation and verification are complete
- Do not wait for CI checks to complete after opening or updating a pull request unless the user explicitly asks
- Report the CI status as pending when checks are still running and return control to the user
- If a merge is requested while required CI checks are pending, do not wait and do not merge; report the pending checks to the user
- Check CI status before merging a pull request
- Treat x64 and ARM64 CI checks as blocking for Foundry changes
- Ignore submit-nuget failures unless the user explicitly asks to investigate that check
- Prefer squash merge when merging Foundry pull requests
- Delete merged feature branches and clean up worktrees after PR merge
- Do not remove a worktree before merge unless the user explicitly approves it
- Write pull request titles in English using Conventional Commits
- Prefer scoped pull request titles when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(readme): ...`
- Write concise pull request descriptions in English
- Include: summary, reason, main changes, and testing notes

Subagent rules:
- Use subagents when the user explicitly asks for them or when parallel read-only analysis materially helps the task
- Use subagents only for read-only code exploration and analysis
- Do not use subagents to modify files
- Do not use subagents to write, add, or refactor logs
- The main agent is responsible for all code edits, logging changes, commits, pushes, and pull requests

Output rules:
- Do not add emojis
- Do not add unnecessary comments
- Only explain decisions when useful
- When making assumptions, choose the most reasonable one and proceed

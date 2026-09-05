You must speak and write code exclusively in English.

General behavior:
- Be concise, direct, and pragmatic
- Prefer implementation over long explanations
- Do not explain obvious things
- Avoid overengineering
- Follow the existing project structure and conventions

Task execution:
- Treat implementation requests as authorization to complete the scoped work, relevant verification, and requested delivery
- Choose reasonable defaults for routine reversible decisions; ask only when missing information materially affects scope, correctness, or an irreversible action
- Continue independent authorized work while awaiting clarification and prepare a reviewable result before requesting necessary approval
- Incorporate follow-up requirements without abandoning the original task unless the user cancels or replaces it
- Respect actual permission boundaries without adding approval steps for hypothetical risks

Instruction handling:
- Follow applicable system and developer instructions; within those boundaries, explicit user instructions take precedence over skill guidance and repository defaults
- For agent workflow defaults, follow current official OpenAI guidance for the model in use over conflicting repository or skill preferences, within the system, developer, and explicit user instructions above
- Apply guidance relevant to the task; distinguish official recommendations from local implementation choices and preserve product contracts, architecture, and security constraints
- Read relevant repository and skill instructions before applying them, and resolve conflicts using the current task context
- If a skill or repository instruction blocks progress, identify the exact file and instruction, explain its relevance, and distinguish an explicit requirement from an interpretation

Skill and documentation tools:
- Use Context7 when implementation or verification depends on library or framework APIs, setup, or version-specific behavior; resolve the relevant library and consult documentation matching the repository version before relying on memory
- Use the relevant Superpowers skill when the task calls for its workflow, such as brainstorming, debugging, planning, implementation, review, or verification; read and apply the selected skill rather than merely naming it
- Use the relevant PostHog skill for Foundry telemetry instrumentation, event contracts, privacy behavior, analytics queries, or PostHog investigations; read and apply the task-specific skill before implementation or tool calls
- Keep telemetry implementation in `Foundry.Telemetry` and relevant tests in `Foundry.Telemetry.Tests`; preserve consent and privacy contracts, and resolve the target PostHog project from verified context before accessing live data
- Keep skill use proportional to the task and follow the instruction precedence above; do not invoke unrelated skills or add unnecessary workflow steps
- If Context7 or a required skill is unavailable, state the limitation and continue with official documentation or an equivalent workflow where possible; do not claim to have used unavailable tools

Verification scope:
- Run the smallest checks that validate the changed behavior, plus all checks explicitly required by this repository
- Broaden or repeat checks only when changes, failures, or unresolved risks justify doing so
- For instruction-only or documentation-only edits, review accuracy, links, and the diff; do not add application tests solely to mirror prose
- Report checks actually run and any limitations; do not claim unverified results

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
- `Foundry.Core`, `Foundry`, `Foundry.Connect`, `Foundry.Deploy`, `Foundry.Localization`, and `Foundry.Telemetry` may consume `Foundry.Utilities` when a capability has a stable cross-project contract.
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
- Respect the native WinUI 3 shell and navigation patterns
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
- Treat the schema versions in the latest published GitHub Release tag as the production baseline; values present only on `main`, pull requests, or intermediate builds are not production versions
- Set each affected schema integer to its value in the source at the latest published release tag plus one; do not increment the product release number or accumulate multiple unreleased schema bumps
- Do not increment a schema version again for additional unreleased changes before the next GitHub Release
- Keep published schema versions monotonic within each contract
- Bump a schema version only when the persisted or generated configuration contract changes in a way that affects runtime behavior or compatibility
- Bump Foundry authoring schema when the user-facing persisted Foundry configuration shape, defaults, migration behavior, or semantic meaning changes
- Bump Foundry.Deploy runtime schema when Deploy consumes new generated configuration, requires new boot media assets, changes the meaning of an existing Deploy field, or needs older boot media to show a compatibility warning
- Bump Foundry.Connect runtime schema when Connect consumes new generated configuration, requires new network provisioning assets, changes the meaning of an existing Connect field, or needs older boot media to show a compatibility warning
- Do not bump schema versions for UI-only changes, localization updates, documentation changes, styling changes, internal refactors, or bug fixes that do not change the configuration contract
- Update the affected constant in `src/Foundry.Core/Models/Configuration/ConfigurationSchemaVersions.cs`; generated and runtime configurations share these constants
- Verify the affected authoring, generator, runtime, and compatibility behavior when changing a schema contract
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
- Test logic in its owning project; move it to `Foundry.Core` only when domain ownership and reuse justify it.

Logging rules:
- Use `FoundryLogConfiguration` for application file sinks so Foundry.OSD, Foundry.Connect, and Foundry.Deploy share the same structured text contract
- Emit UTC timestamps with milliseconds and the Application, Session, and Component context on every application log event
- Keep one stable active log filename with 10 MB size-based rolling and bounded retention
- Use Information as the Foundry.OSD default level, with Debug enabled by developer mode
- Use Debug as the default file level for WinPE Bootstrap, Foundry.Connect, and Foundry.Deploy; keep verbose console output opt-in
- Write logs only when they add operational or diagnostic value
- Use the log levels already defined by the project
- Use Debug for detailed troubleshooting; keep meaningful operation outcomes at Information or above
- Use Information for meaningful lifecycle or business events
- Use Warning for recoverable abnormal states
- Use Error for failed operations that need attention
- Avoid Fatal unless the process cannot continue
- Record one start and one terminal outcome for important user, provisioning, media, and deployment operations
- Keep log messages logical, coherent, and not superfluous
- Do not log noisy UI interactions or obvious control flow
- Sample high-frequency numeric progress instead of logging every callback
- Do not log secrets, tokens, passwords, full query strings, or sensitive user data
- Prefer structured properties when the existing logger supports them
- Correlate Bootstrap, Connect, and Deploy with `FOUNDRY_DIAGNOSTIC_SESSION_ID`
- Persist WinPE logs best-effort without allowing persistence failures to replace the primary application outcome
- Export sanitized support bundles by default without modifying source logs; raw export must require an explicit sensitive-data warning
- Fail closed when a source cannot be sanitized, and publish support archives atomically only after manifest and summary generation succeeds
- Apply these logging rules to all changes, including delegated work; the main agent reviews logging behavior during integration

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
- Use Windows and the .NET 10 SDK for `src/Foundry.slnx`; `.github/workflows/ci.yml` defines the build and test commands
- Run the relevant executable test project with `dotnet run --project src/<Project>.Tests/<Project>.Tests.csproj -c Release -p:Platform=x64`; use `--no-build` only after building that configuration
- The local documentation-only exemption does not skip CI: the current workflow runs Format and dependent x64 and ARM64 builds on PRs
- Before opening or updating a pull request that changes application code, XAML, resources, or formatting configuration, run `scripts\Test-FoundryFormat.ps1` from the repository root
- For documentation-only or instruction-only changes, review the affected prose and links and run `git diff --check`; skip application formatting and runtime tests
- For other changes, such as scripts or workflows, run checks appropriate to the affected files
- If formatting fails in changed files, run `scripts\Format-Foundry.ps1`, review its output, retain only relevant edits, and rerun `scripts\Test-FoundryFormat.ps1`
- If formatting fails solely on unchanged baseline files, report the failure without reformatting unrelated files
- Treat `scripts\Test-FoundryFormat.ps1` as the source of truth for formatting checks
- Include the format check result in pull request testing notes, or explain why it was skipped for the change scope

Git, worktree, and pull request rules:
- Follow Conventional Commits for all commit messages
- Prefer Conventional Commit scopes when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(readme): ...`
- Write commit messages in English
- Keep commits atomic and focused
- Use a dedicated git worktree for implementation work when the task changes code
- Create worktrees outside the main repository folder
- Fetch the remote base before creating a worktree; preserve existing checkout changes and reuse the task worktree for follow-ups
- Create a focused branch for each implementation task
- Keep branch names short, descriptive, and aligned with the task scope
- Do not mix unrelated changes in the same branch
- Push the branch and open a pull request when implementation and verification are complete
- Do not wait for CI checks to complete after opening or updating a pull request unless the user explicitly asks
- Report the CI status as pending when checks are still running and return control to the user
- If a merge is requested while required CI checks are pending, do not wait and do not merge; report the pending checks to the user
- Check CI status before merging a pull request
- Treat x64 and ARM64 CI checks as blocking for Foundry changes
- Assess failures against the actual required checks; do not assume an unfamiliar check is non-blocking
- Merge only when requested and follow the requested merge strategy
- After a requested merge, clean up only the merged task branch and its clean worktree; preserve other work
- Do not remove a worktree before merge unless the user explicitly approves it
- Write pull request titles in English using Conventional Commits
- Prefer scoped pull request titles when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(readme): ...`
- Write concise pull request descriptions in English
- Include: summary, reason, main changes, and testing notes
- Include when relevant: linked issues, breaking or schema changes, screenshots or recordings, and follow-up work
- Remove credentials, tenant data, hardware hashes, device identifiers, and personal information from pull request attachments
- Treat local ARM64 validation as required only for architecture-sensitive changes; both x64 and ARM64 CI checks remain blocking

Subagent rules:
- Delegate bounded, independent analysis, implementation, or verification tasks when parallel work materially improves delivery and the main agent can continue useful work
- Keep simple or tightly coupled tasks local; do not delegate solely to increase agent count
- Assign explicit file or module ownership for edits, avoid overlapping work, and tell subagents to preserve other contributors' changes
- Give each subagent the relevant task context and acceptance criteria; avoid duplicate exploration
- The main agent reviews and integrates delegated changes and owns final verification, commits, pushes, and pull requests

Output rules:
- Lead with the outcome and use plain, concise English
- Prefer short paragraphs; use lists only for steps or genuinely parallel information
- Explain decisions, tradeoffs, and technical details only when they help the user assess the result
- During sustained work, provide brief updates on findings, remaining uncertainty, and the next step
- In the final response, state what changed, relevant verification, and any blocker or required follow-up without repeating the work log
- Do not add emojis
- Do not add unnecessary comments

Instruction guidance source: [OpenAI GPT-6 Astra prompting best practices](https://developers.openai.com/api/docs/guides/latest-model#prompting-best-practices).

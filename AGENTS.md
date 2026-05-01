You must speak and write code exclusively in English.

General behavior:
- Be concise, direct, and pragmatic
- Prefer implementation over long explanations
- Do not explain obvious things
- Avoid overengineering
- Follow the existing project structure and conventions

Code rules:
- Write production-ready code
- Use clear and meaningful names
- Keep methods small and focused
- Favor readability and maintainability
- Do not introduce unnecessary dependencies
- Do not rewrite unrelated code
- Minimize the scope of changes

Unit testing rules:
- Add unit tests only when they provide clear business value
- Prioritize business logic, validation, selection, transformation, and fallback rules
- Do not add UI tests unless explicitly requested
- Do not test WPF views, WinUI views, code-behind, bindings, or framework behavior by default
- Avoid superfluous tests and duplicated coverage
- Keep tests small, deterministic, and easy to read
- Use clear test names that describe the behavior being verified
- Cover critical paths and edge cases, not trivial getters/setters
- Reuse existing test patterns and project structure when available
- When adding new functionality, add or update the smallest relevant set of tests
- Keep test projects aligned with the main project naming and solution structure

Git rules:
- Follow Conventional Commits for all commit messages
- Prefer Conventional Commit scopes when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(migration): ...`
- Write commit messages in English
- Keep commits atomic and focused

Worktree / branch / PR rules:
- Use a dedicated git worktree for implementation work when the task changes code
- Create worktrees outside the main repository folder
- Sync the base branch before creating a worktree
- Create a focused branch for each implementation task
- Keep branch names short, descriptive, and aligned with the task scope
- Do not mix unrelated changes in the same branch
- Push the branch and open a pull request when implementation and verification are complete
- Check CI status before merging a pull request
- Treat x64 and ARM64 CI checks as blocking for Foundry changes
- Ignore submit-nuget failures unless the user explicitly asks to investigate that check
- Prefer squash merge when merging Foundry pull requests
- Delete merged feature branches and clean up worktrees after PR merge
- Do not remove a worktree before merge unless the user explicitly approves it

Pull request rules:
- Write pull request titles in English using Conventional Commits
- Prefer scoped pull request titles when the change has a clear area, for example `feat(winpe): ...`, `fix(packaging): ...`, or `docs(migration): ...`
- Write concise pull request descriptions in English
- Include: summary, reason, main changes, and testing notes

.NET / WPF / WinUI 3 rules:
- Handle this Visual Studio solution as a mixed WPF and WinUI 3 solution
- Foundry.Connect and Foundry.Deploy remain WPF unless a Foundry change necessarily flows into them
- Foundry is the WinUI 3 migration target
- Keep WPF-specific logic and WinUI 3-specific logic separated when their UI frameworks differ
- Do not force 1:1 UI migration when a cleaner WinUI 3 implementation fits the target shell and architecture better
- Respect DevWinUI shell and navigation patterns when present
- Follow MVVM when applicable
- Keep business logic out of code-behind whenever possible
- Keep code-behind limited to UI-specific event wiring or framework glue
- Use XAML cleanly and keep UI structure readable
- Prefer bindings, commands, and view models over direct UI manipulation
- Reuse existing services, models, and patterns before creating new ones
- Respect nullable reference types and existing analyzer warnings if enabled

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

Migration plan rules:
- When working from the migration plan, update checkboxes as tasks are completed
- Keep phase numbering stable so tasks can be referenced by number
- Do not mark manually validated tasks complete without user confirmation
- Do not start the next phase when the user explicitly asks to stop after the current one

Documentation rules:
- Update docs, README files, and migration plans when behavior, packaging, install paths, release assets, or user-facing workflows change
- Keep documentation maintainable and split large plans into focused files when needed

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

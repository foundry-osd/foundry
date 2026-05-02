# Foundry WinUI Migration

This folder is the source of truth for the Foundry WPF to WinUI 3 migration plan.

Use this file as the single prompt entry point. It links to the migration context, architecture contracts, execution phases, checklists, and decision records.

## Start Here

- [Overview](00-overview.md): goals, scope, current state, external guidance, and non-negotiable constraints.
- [Migration Workflow](01-workflow.md): branch strategy, worktrees, parallelization rules, workstreams, and PR sequence.
- [Resolved Decisions](decisions/resolved-decisions.md): decisions already locked for the migration.
- [Open Decisions](decisions/open-decisions.md): remaining unresolved decisions, currently none.

## Architecture References

- [Filesystem Layout](architecture/filesystem-layout.md): `ProgramData`, ISO, USB, `X:\Foundry`, runtime, cache, logs, and no-fallback rules.
- [Page Map And Navigation Contract](architecture/page-map.md): `NavigationView` sections, page responsibilities, ADK-blocked navigation, and blocking operation overlays.

## Execution Phases

- [Foundation Phases](phases/01-foundation.md): phases 1-3.
- [Core Extraction Phases](phases/02-core-extraction.md): phases 4-5.
- [Startup, Distribution, And CI Phases](phases/03-startup-distribution-ci.md): phases 6-8.
- [Localization, Logging, And Shell Phases](phases/04-localization-logging-shell.md): phases 9-11.
- [Media And WinPE Phases](phases/05-media-and-winpe.md): phases 12-13.
- [Business Workflow Phases](phases/06-business-workflows.md): phases 14-16.
- [Compatibility, Cleanup, And Cutover Phases](phases/07-compatibility-cleanup-cutover.md): phases 17-20.

## Checklists

- [Testing Strategy](checklists/testing-strategy.md)
- [Cutover Readiness](checklists/cutover-readiness.md)

## Prompting Guidance

When asking an AI agent to work on this migration, start from this file and name the exact phase or step ID.

Examples:

```text
Use docs/migration/README.md as context. Implement step 12.9 only.
```

```text
Use docs/migration/README.md as context. Review the filesystem layout contract before planning phase 12.
```

Keep implementation work scoped to one phase group or one step ID unless the plan explicitly says the work can run in parallel.

## Agent Execution Rules

- [ ] Guide the maintainer step by step whenever a migration step requires a local action that cannot be automated safely.
- [ ] State the exact command, expected result, and next action for each required maintainer-side step.
- [ ] After completing an implementation step, run the relevant validation commands before claiming the step is complete.
- [ ] Commit completed work automatically with a focused Conventional Commit message.
- [ ] Push the branch automatically after a successful commit.
- [ ] Open the pull request automatically against the planned target branch without asking for extra approval.
- [ ] Do not commit, push, or open a PR when validation fails, when unrelated user changes would be included, or when the requested scope is ambiguous.

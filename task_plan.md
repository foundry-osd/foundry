# Task Plan

## Goal
- Replace the monolithic deployment orchestrator with a step-based pipeline using one class per step.
- Remove the legacy switch-based execution path entirely.
- Clean up dead code in the deployment module and directly impacted UI/DI glue.

## Phases
- [complete] Create planning files and capture baseline findings
- [complete] Introduce shared pipeline primitives and execution context
- [complete] Replace orchestrator logic with step-based coordinator
- [complete] Add and register the 14 deployment step classes
- [complete] Clean up dead code and validate with build

## Risks
- The existing orchestrator mixes shared helpers with step logic; extraction needs careful ownership boundaries.
- Step names are user-visible and must remain byte-for-byte compatible.

## Errors Encountered
- `python` is not available in this shell, so the planning skill session catchup script could not run.

## Outcome
- The switch-based deployment dispatch was removed and replaced with a 14-step pipeline.
- `dotnet build src/Foundry.Deploy/Foundry.Deploy.csproj` completed successfully after the refactor.

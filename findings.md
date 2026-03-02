# Findings

## Baseline
- `src/Foundry.Deploy/Services/Deployment/DeploymentOrchestrator.cs` is the sole implementation of the deployment workflow and contains both orchestration and all 14 step implementations.
- The file duplicates live and dry-run behavior in two separate switch methods.
- `src/Foundry.Deploy/ViewModels/MainWindowViewModel.cs` contains a few hard-coded step names that should be aligned with a shared source of truth.
- `src/Foundry.Deploy/Program.cs` currently registers only `IDeploymentOrchestrator`, not individual deployment steps.

## Constraints
- `IDeploymentOrchestrator` must stay compatible.
- The workflow must keep exactly 14 user-visible steps in the same order.

## Result
- `DeploymentOrchestrator` is now a thin coordinator over injected `IDeploymentStep` implementations.
- The workflow is defined by explicit DI registrations plus a strict runtime validation against `DeploymentStepNames.All`.
- The old `ExecuteStepAsync`, `ExecuteDryRunStepAsync`, step-name array, and private step result type are gone.
- `MainWindowViewModel` now references shared step-name constants for the debug pages that were previously hard-coded.

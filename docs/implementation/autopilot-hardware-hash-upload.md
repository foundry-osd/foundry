# Autopilot Hardware Hash Upload Implementation Plan

This index is the entry point for the Autopilot hardware hash upload plan. The detailed plan is split into smaller documents so implementation agents can load only the context needed for the current phase.

## Required Instructions
- Implementation agents must follow the repository instructions in [AGENTS.md](../../AGENTS.md).
- All PR titles, commit messages, code, and documentation changes must be in English.
- Add XML documentation comments for public or non-obvious C# APIs when they clarify intent, contracts, or operational constraints.
- Each phase branch must branch from `feature/autopilot-hash-upload-foundation` and merge back into that foundation branch before the next phase starts.
- Do not merge, squash, or auto-squash pull requests automatically. The repository owner handles PR merges manually.
- Do not run full solution tests during planning-only updates unless explicitly requested.

## Plan Documents
- [Overview](autopilot-hash-upload/00-overview.md): purpose, branch strategy, PR roadmap, non-goals, resolved decisions, and references.
- [Feasibility And Current State](autopilot-hash-upload/01-feasibility-current-state.md): WinPE feasibility, constraints, current Foundry flow, and target data flow.
- [UX And Runtime Model](autopilot-hash-upload/02-ux-runtime-model.md): Foundry OSD UX, Foundry Deploy UX, and proposed configuration/runtime records.
- [Security And Graph](autopilot-hash-upload/03-security-graph.md): certificate handling, permission split, Graph import shape, and request rules.
- [WinPE And Deploy Workflow](autopilot-hash-upload/04-winpe-deploy-workflow.md): OA3Tool strategy, `PCPKsp.dll`, retained artifacts, failure taxonomy, and ownership boundaries.
- [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md): phase-by-phase checklist with PR titles and manual checks.
- [Validation Risk And Documentation](autopilot-hash-upload/06-validation-risk-docs.md): automated test matrix, physical validation matrix, risk register, Docusaurus/user documentation deliverables.

## Phase Order
| Order | Branch | Pull request title | Detail |
| --- | --- | --- | --- |
| 0 | `feature/autopilot-hash-upload-foundation` | `docs(autopilot): plan hardware hash upload from WinPE` | [Overview](autopilot-hash-upload/00-overview.md) |
| 1 | `feature/autopilot-hash-upload-config` | `feat(autopilot): add provisioning mode configuration` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-1-configuration-model) |
| 2 | `feature/autopilot-hash-upload-security` | `feat(autopilot): add secure tenant upload onboarding` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-2-security-and-tenant-onboarding) |
| 3 | `feature/autopilot-hash-upload-ui` | `feat(autopilot): add provisioning method selection` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-3-autopilot-page-ux) |
| 4 | `feature/autopilot-hash-upload-media` | `feat(winpe): stage autopilot hash capture assets` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-4-media-build-and-winpe-assets) |
| 5 | `feature/autopilot-hash-upload-runtime` | `feat(deploy): branch autopilot runtime by provisioning mode` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-5-foundry-deploy-runtime-branching) |
| 6 | `feature/autopilot-hash-upload-capture` | `feat(deploy): capture autopilot hardware hash in WinPE` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-6-hash-capture-service) |
| 7 | `feature/autopilot-hash-upload-graph` | `feat(autopilot): import hardware hashes with Graph` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-7-graph-upload-service) |
| 8 | `feature/autopilot-hash-upload-docs` | `docs(autopilot): document WinPE hardware hash upload` | [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md#phase-8-documentation-and-release-guardrails) |

## Implementation Progress Tracker
Use this table as the cross-phase implementation status board. Detailed task, automated test, and manual checkboxes live in [Implementation Phases](autopilot-hash-upload/05-implementation-phases.md).

| Phase | Branch created | Implementation complete | Verification complete | Manual checks complete | PR opened | Merged back |
| --- | --- | --- | --- | --- | --- | --- |
| 0 Foundation | [x] | [x] | [x] | [x] | [ ] | [ ] |
| 1 Configuration model | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 2 Security and tenant onboarding | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 3 Autopilot page UX | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 4 Media build and WinPE assets | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 5 Foundry Deploy runtime branching | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 6 Hash capture service | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 7 Graph upload service | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |
| 8 Documentation and release guardrails | [ ] | [ ] | [ ] | [ ] | [ ] | [ ] |

## Documentation Reminder
Phase 8 must update the Docusaurus documentation when the implemented behavior affects user-facing OSD, Deploy, WinPE requirements, setup, troubleshooting, permissions, or release notes. The Docusaurus repository is `E:\Github\Foundry Project\foundry-osd.github.io`; create a dedicated worktree and branch for that repository before editing documentation.

# Task Plan: Foundry.Deploy Consistency Audit

## Goal
Audit `src/Foundry.Deploy` for workflow coherence (UI → orchestrator → services), debug-safe behavior, cache/ISO/USB handling, autopilot, and hidden state risks; deliver severity-ordered findings with file/line references and action-oriented recommendations.

## Current Phase
Phase 1

## Phases
### Phase 1: Gather Context & Define Scope
- [x] Inventory key layers (UI, orchestrator, services) relevant to deployment flow
- [x] Identify expected safety checks (debug dry-run, confirmations, cache modes, autopilot) from documentation/assumptions
- [x] Capture any existing gaps or uncertainties in `findings.md`
- **Status:** complete

### Phase 2: Code Review Analysis
- [ ] Trace UI → orchestrator → service calls, noting mismatches or missing state validations
- [ ] Examine safety modes (debug safe, confirmation dialogs, autopilot fallbacks) for edge cases
- [ ] Evaluate cache handling (USB partition detection, ISO mode, autopilot caching, logs) for destructive/resume risks
- [ ] Note any hidden state/flags that could leave the system in inconsistent states (resumes, failures, non-validated transitions)
- **Status:** in_progress

### Phase 3: Summarize Findings
- [ ] Rank issues by severity (blocking, major, minor)
- [ ] Reference affected files/lines per issue
- [ ] Provide concise recommendations for each finding
- [ ] Update `findings.md` and produce final response
- **Status:** pending

## Key Questions
1. Are there undocumented side-effects between UI state (MainWindowViewModel) and orchestrator services (DeploymentOrchestrator) that can leave destructive actions unconfirmed?
2. Does debug safe mode fully isolate `WindowsDeploymentService` actions, especially when Autopilot or cache writes still run?
3. Are cache paths for ISO/USB and Autopilot state persisted/resumed safely across restarts or failures?
4. Are there any unvalidated state transitions (e.g., autopilot flagged as complete before hash exported) that could hide failures?

## Notes
- Previous architecture plan activity is archived in `findings.md` and `progress.md`; this audit reuses that context but focuses on verifying the implemented behavior.

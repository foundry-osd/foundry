# Business Workflow Phases

## Phase 14: Expert Configuration Workflow

**Priority:** medium.

**Goal:** port expert mode without mixing it into the standard workflow.

- [ ] **14.1** Port expert sections without exposing `General` as an Expert navigation page:
  - [ ] Network.
  - [ ] Localization.
  - [ ] Autopilot.
  - [ ] Customization.
  - [ ] Keep `General` in the General navigation section.
  - [ ] Preserve serialization of the existing expert document `general` section when required by schema compatibility.
- [ ] **14.1.1** Keep expert `Localization` scoped to OS deployment localization, not WinPE boot language:
  - [ ] Port WPF `LocalizationSettingsViewModel` behavior for deployment language selection.
  - [ ] Preserve `LocalizationSettings.VisibleLanguageCodes`.
  - [ ] Preserve `LocalizationSettings.DefaultLanguageCodeOverride`.
  - [ ] Preserve `LocalizationSettings.DefaultTimeZoneId`.
  - [ ] Preserve `LocalizationSettings.ForceSingleVisibleLanguage`.
  - [ ] Preserve `DeployConfigurationGenerator` mapping to `DeployLocalizationSettings` for `Foundry.Deploy`.
  - [ ] Do not move `GeneralSettings.WinPeLanguage` into the expert `Localization` page.
- [ ] **14.2** Port configuration import.
- [ ] **14.3** Port configuration export.
- [ ] **14.4** Port deploy configuration export.
- [ ] **14.5** Preserve JSON defaults.
- [ ] **14.6** Preserve validation behavior.
- [ ] **14.7** Preserve schema compatibility.
- [ ] **14.8** Add tests only where business logic changed.
- [ ] **14.9** Commit:

```powershell
git commit -m "feat(configuration): port expert configuration workflow"
```

**Validation**

- [ ] **14.10** Import existing expert config from WPF app.
- [ ] **14.11** Export expert config from WinUI app.
- [ ] **14.12** Compare normalized JSON output with WPF reference for the same input.
- [ ] **14.13** Export deploy config and validate `Foundry.Deploy` can consume it.

## Phase 15: Network Provisioning Workflow

**Priority:** medium.

**Goal:** port network and Wi-Fi provisioning logic used by Foundry.Connect handoff.

- [ ] **15.1** Port network settings model bindings.
- [ ] **15.2** Port Wi-Fi settings validation.
- [ ] **15.3** Port 802.1X settings.
- [ ] **15.4** Port certificate picker through WinUI shell service.
- [ ] **15.5** Port provisioning bundle creation.
- [ ] **15.6** Preserve `Foundry.Connect` configuration schema.
- [ ] **15.7** Preserve asset file preparation behavior.
  - [ ] **15.7.1** Use explicit WinUI `PasswordBox` handling for Wi-Fi and network secrets.
  - [ ] **15.7.2** Never log network secrets.
  - [ ] **15.7.3** Never display network secrets in the Start summary.
  - [ ] **15.7.4** Serialize secrets only when required by the runtime or configuration contract.
- [ ] **15.8** Commit:

```powershell
git commit -m "feat(network): port connect provisioning workflow"
```

**Validation**

- [ ] **15.9** Existing `FoundryConnectProvisioningServiceTests` pass.
- [ ] **15.10** Generated `Foundry.Connect` configuration matches WPF reference for equivalent settings.
- [ ] **15.11** Certificate asset copy behavior is preserved.

## Phase 16: Autopilot And Customization Workflows

**Priority:** medium-low.

**Goal:** port remaining expert workflow features after core media creation is functional.

- [ ] **16.1** Port Autopilot profile selection.
- [ ] **16.2** Port Autopilot profile import/selection dialog.
  - [ ] **16.2.1** Use the blocking operation overlay for profile import.
  - [ ] **16.2.2** Keep Microsoft Graph authentication in the WinUI `Foundry` app/infrastructure layer.
  - [ ] **16.2.3** Keep `InteractiveBrowserCredential`, token cache behavior, environment-variable-driven Graph configuration, and Graph HTTP calls out of `Foundry.Core`.
  - [ ] **16.2.4** Move only pure Autopilot conversion, validation, and file transformation logic to `Foundry.Core` when useful.
- [ ] **16.3** Port customization settings.
- [ ] **16.3.1** Use the blocking operation overlay for Autopilot tenant download.
- [ ] **16.4** Preserve generated deploy configuration output.
- [ ] **16.5** Preserve profile file embedding into WinPE media.
- [ ] **16.6** Commit:

```powershell
git commit -m "feat(autopilot): port autopilot and customization workflows"
```

**Validation**

- [ ] **16.7** Existing Autopilot-related tests pass.
- [ ] **16.8** Exported deploy config includes selected profiles.
- [ ] **16.9** WinPE media includes expected profile payload.

# Findings: Foundry WinUI 3 Migration Study

## Requirements
- Migrate only `src/Foundry` from WPF to WinUI 3 on .NET 10.
- Keep `src/Foundry.Connect` and `src/Foundry.Deploy` as WPF projects.
- Analyze solution/build topology, Foundry migration surface, architecture quality, cross-project impact, packaging/publish/release behavior, WinUI 3 best practices, migration strategy, risks, and validation decisions.
- Do not implement or modify code during this phase.
- Planning artifacts may be written and checkpointed.

## Repository State
- Primary checkout: `E:\Github\Foundry Project\foundry`.
- Dedicated worktree: `E:\Github\Foundry Project\foundry-winui3-migration-study`.
- Current worktree branch: `codex/winui3-migration-study`.
- Worktree was created from `main` at `46682845972ad677642cef7d986ad8e82b12a65e`; current HEAD contains plan-only commits.

## Research Findings
- Solution contains 3 app projects and 3 test projects in `src/Foundry.slnx`: `Foundry`, `Foundry.Connect`, `Foundry.Deploy`, and their matching test projects.
- `src/Directory.Build.props` globally sets `TargetFramework=net10.0-windows`, `UseWPF=true`, `EnableWindowsTargeting=true`, `RuntimeIdentifiers=win-x64;win-arm64`, `ApplicationIcon=Assets\Icons\app.ico`, and `PackageIcon=app.ico`.
- All three app projects inherit the global WPF settings and declare `OutputType=WinExe` plus explicit `StartupObject`.
- All three app projects use the same WPF custom-entrypoint pattern: `<ApplicationDefinition Remove="App.xaml" />` and `<Page Include="App.xaml" />`.
- Test projects override `UseWPF=false`, clear app icons and runtime identifiers, and project-reference only their matching app.
- `Foundry.Deploy` links `..\Foundry\Assets\7z\**\*` into its output, creating a concrete cross-project asset dependency on the `Foundry` source tree.
- Context7 resolved current Microsoft Windows App SDK documentation as `/websites/learn_microsoft_en-us_windows_windows-app-sdk`.
- Context7 notes relevant to this migration: Windows App SDK runtime initialization differs for packaged versus unpackaged/external-location apps; unpackaged apps rely on bootstrap/runtime availability; WinUI uses DispatcherQueue, AppWindow/windowing APIs, XamlRoot-sensitive dialogs/pickers, and Windows App SDK deployment/runtime mechanics rather than WPF Application/Dispatcher/resource behavior.
- `src\Foundry\Program.cs` uses a custom `[STAThread]` entrypoint, builds a `Microsoft.Extensions.Hosting` host, resolves `App` and `MainWindow` from DI, calls `app.InitializeComponent()`, and runs the WPF window through `app.Run(mainWindow)`.
- `src\Foundry\App.xaml` depends on WPF `Application.Resources`, `ResourceDictionary.MergedDictionaries`, and `PresentationFramework.Fluent` pack URIs.
- `src\Foundry\MainWindow.xaml` is the main WPF shell: `Window`, `Menu`, WPF `DataTemplate` by viewmodel type, expert-mode `ListBox` navigation, `ContentControl` swaps, status/progress footer, dynamic Fluent resources, and WPF layout primitives.
- `src\Foundry\MainWindow.xaml.cs` is small but WPF-specific: derives from `Window`, sets `DataContext`, wires `Loaded`, refreshes USB candidates, and triggers startup update checks.
- `src\Foundry\Services\ApplicationShell\IApplicationShellService.cs` leaks WPF through `System.Windows.MessageBoxImage`, so the abstraction is not UI-framework-neutral.
- `src\Foundry\Services\ApplicationShell\ApplicationShellService.cs` owns WPF shutdown, modal windows, `Microsoft.Win32` file/folder dialogs, WPF `MessageBox`, owner resolution through `Application.Current.Windows`, and WPF dispatcher invocation.
- `src\Foundry\Services\Theme\ThemeService.cs` directly mutates WPF `Application.Current.Resources.MergedDictionaries` and swaps `PresentationFramework.Fluent` dictionaries.
- `src\Foundry\ViewModels\LocalizedViewModelBase.cs`, `src\Foundry\ViewModels\MainWindowViewModel.cs`, and `src\Foundry\Services\Operations\OperationProgressService.cs` use WPF `Dispatcher`/`Application.Current.Dispatcher`; this is a key adaptation point.
- `src\Foundry\Views\ReleaseNotesMarkdownDocumentBuilder.cs` is WPF document infrastructure (`FlowDocument`, `Paragraph`, `Run`, `Hyperlink`, `Brush`, `Application.Current.TryFindResource`) and should not be ported mechanically.
- Foundry view code-behind is mostly thin, with two notable UI-specific bridges: `NetworkSettingsView.xaml.cs` manually syncs WPF `PasswordBox`, and `UpdateAvailableDialog.xaml.cs` builds a WPF `FlowDocument` when DataContext changes.
- Release workflow publishes Foundry as single-file self-contained WPF output, then copies only `Foundry.exe` to `Foundry-x64.exe` / `Foundry-arm64.exe`; this exact artifact shape conflicts with current Microsoft guidance that unpackaged WinUI 3 apps cannot produce a single-file EXE.
- Release workflow and WinPE services hardcode Connect/Deploy archive names and executables; these must remain stable because Connect/Deploy stay WPF and are launched inside WinPE.
- Official Microsoft docs checked by web search confirm: WinUI 3 unpackaged apps use `<WindowsPackageType>None</WindowsPackageType>` auto-initialization or explicit bootstrapper, depend on Windows App SDK runtime unless self-contained, and cannot produce a single-file EXE when distributed unpackaged.

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Treat plan files as the only allowed write scope | Keeps planning durable without violating implementation hard stops. |
| Replace Foundry single-file `.exe` release artifacts with Velopack MSI distribution | User validated that Foundry can stop shipping as `Foundry-x64.exe` / `Foundry-arm64.exe`. Velopack documentation confirms `vpk pack` packages a published app folder and `--msi` generates an MSI alongside normal Velopack release assets. |
| Keep Foundry unpackaged; do not use MSIX | User explicitly rejected MSIX. This aligns with keeping a traditional desktop distribution path while avoiding WinUI single-file publish limitations. |
| Keep `Foundry.Connect` and `Foundry.Deploy` release artifacts unchanged | Their archive and executable names are runtime contracts for WinPE bootstrap and embedding. Velopack applies to the desktop `Foundry` app only. |
| Redesign the Foundry shell using WinUI `NavigationView` while preserving the current conceptual pages | User wants a WinUI redesign with a NavigationView and continuity with the current WPF page model. Current expert sections already map cleanly to pages: General, Network, Localization, Autopilot, Customization. |
| Keep the project name `Foundry` | User clarified that the whole `Foundry` project should migrate and retain its name. This is not a side-by-side replacement with a new project name. |
| Keep `.resx` for now, with WinUI-specific adaptation | User prefers keeping `.resx`. Current implementation uses `ResourceManager`, `StringsWrapper`, runtime culture switching, and service/viewmodel access, which makes `.resx` a pragmatic initial choice. `.resw` should be considered only for WinUI XAML `x:Uid`, manifest localization, or PRI/MRT-specific needs. |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| Initial plan artifacts were accidentally created in the primary checkout | Removed only those plan artifacts and recreated them in the dedicated worktree before starting repository analysis. |

## Resources
- `E:\Github\Foundry Project\foundry-winui3-migration-study\task_plan.md`
- `E:\Github\Foundry Project\foundry-winui3-migration-study\findings.md`
- `E:\Github\Foundry Project\foundry-winui3-migration-study\progress.md`
- `src\Foundry.slnx`
- `src\Directory.Build.props`
- `src\Directory.Solution.props`
- `src\Foundry\Foundry.csproj`
- `src\Foundry.Connect\Foundry.Connect.csproj`
- `src\Foundry.Deploy\Foundry.Deploy.csproj`
- `src\Foundry.Tests\Foundry.Tests.csproj`
- `src\Foundry.Connect.Tests\Foundry.Connect.Tests.csproj`
- `src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj`
- Microsoft Windows App SDK docs via Context7: `/websites/learn_microsoft_en-us_windows_windows-app-sdk`

## Source Inventory Notes
- Global WPF is the first confirmed build-topology blocker for a mixed WinUI 3 + WPF solution. The clean target likely requires project-local or conditional UI framework properties instead of unconditional `UseWPF=true`.
- `Foundry` conversion will need project-file changes later, but implementation is explicitly not started in this phase.
- App-to-app references are absent in project files, but source-level and runtime packaging coupling still exists through scripts, embedded bootstrap logic, and `Foundry.Deploy` linked assets.
- Foundry's reusable core is concentrated in models, configuration services, WinPE services, ADK services, Autopilot services, localization data, logging, and update-check HTTP parsing. These should be preserved where possible.
- Foundry's primary migration surface is the app shell, XAML, dialogs, shell service, theme service, dispatcher abstraction, converters, release-notes rendering, and publish/deployment shape.
- `Foundry.Connect` and `Foundry.Deploy` packaging contracts are not optional UI details. The bootstrap script and WinPE embedding services depend on `Foundry.Connect.exe`, `Foundry.Deploy.exe`, `Foundry.Connect-win-x64.zip`, `Foundry.Connect-win-arm64.zip`, `Foundry.Deploy-win-x64.zip`, and `Foundry.Deploy-win-arm64.zip`.

## External Documentation Notes
- Context7 confirmed Windows App SDK deployment/runtime APIs to consider: `DeploymentManager.Initialize`, `MddBootstrapInitialize`/`MddBootstrapInitialize2`, AppWindow, and WinUI XAML initialization APIs.
- Official Microsoft docs verified:
  - `UseWinUI`, `WindowsPackageType`, `EnableMsixTooling`, `WindowsAppSdkSelfContained`, and related auto-initializer properties are documented Windows App SDK project properties.
  - Unpackaged WinUI 3 apps require Windows App SDK runtime availability or self-contained Windows App SDK output.
  - Single-project MSIX supports only a single executable in the generated MSIX package.
  - WPF-to-WinUI guidance maps WPF `Dispatcher` to WinUI `DispatcherQueue`, `DynamicResource` to `ThemeResource`, `FlowDocumentScrollViewer` to `RichTextBlock`, `Application.Current.MainWindow` to an owned window reference, and `Window` customization to `AppWindow`.
  - For WinUI desktop pickers/message dialogs, APIs that depend on CoreWindow need HWND association; `ContentDialog` needs `XamlRoot`.
- Velopack documentation verified:
  - `vpk pack` packages a previously compiled/published application directory using `--packId`, `--packVersion`, `--packDir`, and `--mainExe`.
  - Windows releases produce update/package assets such as full `.nupkg`, optional delta `.nupkg`, portable zip, setup executable, release indexes, and build asset indexes.
  - MSI generation is enabled with `--msi`; the MSI is built alongside `Setup.exe`.
  - Velopack MSI installs the same app layout as Setup: an install folder with `current`, `Update.exe`, and an execution stub.
  - CI release flows commonly download the previous release before packing so delta packages and release indexes can be generated.
  - For self-contained .NET apps, Velopack advises not to bootstrap the .NET runtime with `--framework`; for framework-dependent apps, `--framework net<version>-<arch>-desktop` is available.
  - Every unique OS/RID should have its own Velopack channel, so Foundry x64 and ARM64 should use separate channels.
  - Velopack GitHub delta support expects any one GitHub release to contain only one full package and one delta package. This makes multi-architecture deltas a proof-required feature for the first rollout.

## Decision Refinement - 2026-04-29

### Foundry distribution through Velopack MSI
- User validated that Foundry can stop shipping as `Foundry-x64.exe` / `Foundry-arm64.exe`.
- Target direction is unpackaged WinUI 3 distributed by Velopack-generated `.msi`.
- MSIX is rejected.
- This resolves the previous highest-risk decision: a single-file executable artifact is no longer required for Foundry.
- New release model should treat Foundry as an installable/updatable desktop app instead of a standalone executable download.
- Velopack is not just an MSI generator. It also introduces update packages, release indexes, and optional delta packages. The release workflow must account for those artifacts rather than uploading only one MSI.
- Current release workflow hardcodes Foundry `.exe` artifacts in `.github/workflows/release.yml` and README badges. Those will need to change in implementation.
- Connect/Deploy should remain on the existing WPF self-contained single-file zip release model because their artifacts are consumed by WinPE bootstrap code and local embedding services.

### Foundry release workflow implications
- Current Foundry publish path shares one `PublishSingleFile=true` property set with Connect/Deploy.
- Future workflow should split publishing into:
  1. Foundry WinUI publish folder for Velopack input.
  2. Velopack pack/upload path for Foundry MSI and update assets.
  3. Existing Connect/Deploy publish and zip path.
- Velopack `packId` is locked as `FoundryOSD.Foundry`.
- Velopack user-facing title remains `Foundry`; author/publisher can use `Foundry OSD`.
- MSI install scope is locked as `PerMachine` because Foundry requires administrator privileges and writes shared workspaces under ProgramData.
- Foundry should publish as self-contained, unpackaged, non-single-file, runtime-specific output for `win-x64` and `win-arm64`.
- Code signing is explicitly out of scope for the migration plan for now. Do not block implementation on signing; keep it as later release hardening.
- Current `ApplicationUpdateService` checks GitHub Releases manually. With Velopack, this should be redesigned around Velopack update APIs instead of only opening a GitHub release page, unless the first implementation intentionally defers in-app update installation.
- Public GitHub releases must be created or published only after Foundry Velopack assets and Connect/Deploy WinPE zips are built and validated. This avoids broken `latest` releases that would break desktop updates and WinPE bootstrap.
- Keep one public GitHub release per Foundry version. The release must include both Foundry Velopack desktop assets and the unchanged Connect/Deploy zips.
- Use Velopack channels `win-x64-stable` and `win-arm64-stable`.
- Delta packages are allowed only after implementation proves the GitHub Releases + multi-architecture channel behavior is safe. If proof fails, disable deltas for the first rollout.

### WinUI shell direction
- User wants a real WinUI redesign, not a one-to-one WPF XAML port.
- User refined the shell direction after review:
  - use `NavigationView` as the primary shell,
  - remove Standard/Expert mode switching,
  - show all pages in navigation,
  - organize pages into a General section and an Expert section,
  - use a Start page for summary and ISO/USB creation,
  - use NavigationView footer entries for Settings and About,
  - expose Logs from Settings as an open-folder action,
  - do not keep a top `MenuBar`.
- General section pages:
  - `Home`: operational dashboard with readiness, ADK state, selected basics, USB detection, and links to required pages.
  - `ADK`: ADK install/status/upgrade page with operation status.
  - `Configuration`: media target and general build inputs: ISO path, USB disk, architecture, WinPE language, drivers, and advanced media options.
  - `Start`: readiness summary, key configuration review, import/export actions, and Create ISO/Create USB.
- Expert section pages:
  - `Network`
  - `Localization`
  - `Autopilot`
  - `Customization`
- Start page summary should show readiness and key config, including blocking issues, warnings, output target, architecture, WinPE language, network, localization, Autopilot, and customization state.
- Validation should be shown inline on owning pages and aggregated on Start with links back to the relevant pages.
- Import/export configuration actions should live on Start, close to review/build actions.
- The new UI should follow WinUI best practices for a desktop operational app: native controls, practical density, clear command hierarchy, and minimal decorative surface.

### Localization direction
- Current Foundry localization is implemented with `.resx`, `ResourceManager`, `StringsWrapper`, `ILocalizationService`, and runtime `LanguageChanged`.
- This model is deeply used by XAML bindings, viewmodels, and services. It supports service-level error messages and formatting in business logic.
- Keeping `.resx` is viable and lower risk for the first migration because it avoids translating every UI key into `.resw` while also preserving service localization behavior.
- WinUI best practice usually favors `.resw` + `ResourceLoader` + `x:Uid` for static XAML and manifest strings. However, Foundry is unpackaged and heavily MVVM/service-driven, so `.resw` should not be introduced blindly.
- Recommended initial approach: keep `.resx` as the source of truth and adapt the binding notification and dispatcher plumbing to WinUI. Revisit `.resw` only for static XAML localization, manifest metadata, or if a proof of concept shows a clear maintenance benefit.
- Runtime language switching remains important. A `.resw` migration may make instant runtime switching harder unless the app introduces explicit page reload or restart behavior.

### Project scope clarification
- The migration target remains the existing `src/Foundry` project name and identity.
- "Shared non-UI library" means extracting framework-agnostic models/services into a separate class library to reduce UI-project coupling. This is not required by the user's stated goal and should not be introduced up front unless the migration proves it is necessary.
- The first implementation should migrate the entire `Foundry` app project in place while keeping scope tightly limited to Foundry and required build/release changes.

## WinUI Shell and UX Specification - 2026-04-29

### NavigationView structure
- Use a single WinUI `NavigationView` shell.
- Do not include a top `MenuBar`.
- Navigation groups:
  - General: `Home`, `ADK`, `Configuration`, `Start`.
  - Expert: `Network`, `Localization`, `Autopilot`, `Customization`.
  - Footer: `Settings`, `About`.
- `Settings` should be a full page, not a compact dialog or flyout.
- `Logs` moves into `Settings` as a read-only card/action that opens the log folder.
- `About` remains a product/about surface with version and product links only.
- Manual update checks and update status live only in Settings.

### Mode removal and generation contract
- Remove the current Standard/Expert mode switch from the UI model.
- Remove mode as a generation gate. Today `IsExpertMode` decides whether deploy configuration and Autopilot profiles are embedded. In the WinUI plan, ISO/USB generation always uses the full configuration model.
- Default/empty expert pages must produce safe default behavior.
- Always write the deploy configuration and Autopilot payload using the full configuration model and safe defaults.
- Validate `Foundry.Connect` and `Foundry.Deploy` against the always-full-config output. They remain WPF, but their config parsing/behavior may need targeted adaptation if the generated configuration contract changes.
- Validation must explicitly cover no config file, default/empty config file, populated config file, Autopilot folder absent, and Autopilot folder present.
- The future viewmodel should expose page state and navigation state directly rather than using `IsExpertMode`, `IsStandardMode`, and `ExpertSections` as the primary shell model.

### Page ownership
- `Home`:
  - Operational dashboard.
  - Read-only status page; no editable configuration fields.
  - Shows global readiness: ADK compatibility, selected architecture/language, USB detection count/status, and whether Start is blocked.
  - Provides navigation links to ADK, Configuration, and Start.
- `ADK`:
  - Owns installed/missing/incompatible state.
  - Owns install and upgrade actions.
  - Shows ADK operation status/progress when ADK work is running.
- `Configuration`:
  - Owns ISO output path.
  - Owns selected USB disk and refresh action.
  - Owns architecture, WinPE language, driver vendor toggles, CA 2023 signature option, USB partition style, USB format mode, and custom driver directory.
- `Network`, `Localization`, `Autopilot`, `Customization`:
  - Keep the current domain boundaries, but use WinUI controls and page layouts.
  - Keep page-local commands on the owning page, such as Autopilot import/download/remove and network certificate pickers.
- `Start`:
  - Owns final readiness review.
  - Owns import/export expert/deploy configuration actions.
  - Owns Create ISO and Create USB actions.
  - Shows blocking issues and warnings with navigation links to owning pages.
  - Does not own editable configuration fields.
  - After importing configuration, navigate to Start so the user sees readiness, warnings, and next actions.
- Do not keep the current persistent shell footer/status surface. Show readiness, USB count, version, and operation state on Home/Start or in the locked operation dialog.

### Operation dialog
- Clicking Create USB first shows the destructive USB confirmation.
- After confirmation, or immediately for Create ISO, open a locked WinUI `ContentDialog`.
- The dialog should use the active window/page `XamlRoot`.
- The dialog blocks navigation and settings edits while the operation is running.
- Dialog content for first migration:
  - operation title,
  - current status,
  - progress bar,
  - Cancel button,
  - terminal result text after success/failure/cancel,
  - Close button only after a terminal state.
- Do not include live logs in the first migration.
- On completion, keep the dialog open with the result instead of auto-closing.
- App-owned dialogs should become WinUI `ContentDialog` surfaces:
  - USB destructive confirmation,
  - ISO/USB operation progress,
  - update prompt,
  - generic notifications/errors,
  - Autopilot profile selection when it remains a modal task,
  - About if it remains modal rather than a full page.
- File and folder selection should use OS pickers initialized with the active window handle.

### Cancellation semantics
- Support Cancel for both ISO and USB.
- Cancellation guarantee is best-effort safe stop:
  - request cancellation through `CancellationToken`,
  - stop before starting new steps,
  - allow cooperative subprocess cancellation where already supported,
  - clean temporary workspace where possible,
  - clearly report if manual cleanup may be required.
- Do not promise strict rollback of USB media to the previous state.
- The implementation should audit all media paths that currently accept `CancellationToken`, then add viewmodel-owned operation cancellation tokens and terminal canceled state.

### Settings and updates
- Settings first migration scope:
  - theme,
  - app UI language,
  - manual update check,
  - update status,
  - logs folder link,
  - cache/temp locations,
  - basic diagnostics.
- Velopack update UX:
  - Use architecture-specific stable channels: `win-x64-stable` and `win-arm64-stable`.
  - Check for updates on startup in the background when the app is installed through Velopack.
  - If an update is available, show a ContentDialog with version and release notes.
  - User explicitly chooses to download/install.
  - After download, show an explicit `Restart and update` action.
  - Use Velopack apply-and-restart behavior only after user confirmation.
  - Manual update check lives in Settings.
  - Non-Velopack local/dev builds should disable update install actions gracefully, show a clear not-installed status in Settings, and never fail startup.
- The existing GitHub-release-based update service should be replaced or adapted to Velopack update APIs during implementation, not carried forward as the primary update mechanism.

### Language scope
- `App language`: application shell/page UI language. It belongs in Settings and should live-refresh normal pages and NavigationView labels. Already-open modal dialogs may refresh on reopen.
- `WinPE language`: boot media language. It belongs on Configuration.
- `Deployment languages/time zone`: target OS deployment localization. It belongs on the Localization expert page.

### Readiness and validation model
- Add a central readiness issue model during implementation.
- Each page should contribute blockers and warnings with:
  - severity,
  - localized title/message,
  - owning page target,
  - optional target field/action.
- Start aggregates readiness issues and provides navigation links back to owning pages.
- Command enablement should still prevent unsafe actions, but the Start page should explain why an action is blocked.

### WinUI documentation notes
- Context7 confirmed `NavigationView` supports binding item sources and left-pane navigation.
- Context7 confirmed WinUI dialog work must be rooted in an active `XamlRoot`.
- Context7 confirmed WinUI/Microsoft.UI.Xaml binding uses standard `INotifyPropertyChanged`; null property names should not be used as WPF-style global refresh notifications.
- Dispatcher usage should move from WPF `Dispatcher` to a WinUI-compatible dispatcher abstraction backed by `DispatcherQueue`.

## Windows Community Toolkit and UI Verification Policy - 2026-04-29

### Windows Community Toolkit usage
- User wants to use Windows Community Toolkit in the WinUI migration.
- Current repository already uses `CommunityToolkit.Mvvm` in all three application projects. Keep this MVVM baseline.
- For Foundry WinUI UI controls, native WinUI controls remain the default. Use Windows Community Toolkit controls where they provide a clear fit, reduce custom UI code, or improve consistency.
- Do not add broad Toolkit dependencies preemptively. Add componentized Toolkit packages only when the implementation uses them.
- First approved Toolkit UI package target: `CommunityToolkit.WinUI.Controls.SettingsControls`.
- First approved Toolkit controls:
  - `SettingsCard`
  - `SettingsExpander`
- Primary planned use: full Settings page, including theme, app UI language, update status/check, logs, cache/temp locations, and diagnostics.
- Secondary possible use: ADK diagnostics or environment-readiness sections if Settings-style grouping fits better than native cards.
- Context7 confirmed Windows Community Toolkit has WinUI 3 / Windows App SDK package families such as `CommunityToolkit.WinUI.Controls.SettingsControls`, `CommunityToolkit.WinUI.Controls.Segmented`, and `CommunityToolkit.WinUI.Controls.HeaderedControls`.

### Implementation-time app execution policy
- The implementation plan should explicitly allow and require the implementing agent to run Foundry locally during the migration to verify UI layout and behavior.
- Run the app after every migrated page or major dialog, not only after the final build.
- Required run checkpoints:
  - WinUI shell + `NavigationView`
  - `Home`
  - `ADK`
  - `Configuration`
  - `Network`
  - `Localization`
  - `Autopilot`
  - `Customization`
  - `Start`
  - `Settings`
  - `About`
  - update dialogs
  - ISO/USB progress dialog
- Each app run should verify:
  - layout at the default desktop size,
  - resizing down to the minimum intended window size,
  - NavigationView selection and footer behavior,
  - theme switching,
  - language switching,
  - disabled/enabled states during operations,
  - inline validation and Start page validation summary,
  - dialog ownership and blocking behavior,
  - ContentDialog placement through the active `XamlRoot`.
- The implementation should use screenshots or a concise manual verification note for each page checkpoint. UI fixes should be made immediately when layout, text clipping, theme, or navigation problems are found.
- This run policy does not change the planning-phase hard stop; it applies to the later approved implementation phase.

## Deep Plan Audit Closure - 2026-04-29

### Confirmed dark spots from the re-audit
- The planning artifacts contained stale contradictions:
  - `findings.md` still listed Foundry packaging as unknown after Velopack MSI was selected.
  - `progress.md` still reported the current location as Phase 1 even though later phases were complete.
- Release topology was under-specified. A desktop-only Velopack release would break current WinPE bootstrap and embedding paths because they resolve GitHub `latest` and fixed Connect/Deploy zip names.
- The existing release workflow creates the GitHub release before all assets are built and uploaded. This can publish a broken `latest` release.
- Velopack multi-architecture update behavior needs explicit channels and proof for deltas.
- The current plan said "Settings language" without distinguishing app UI language, WinPE language, and deployment localization.
- The Home/Configuration/Start split needed an explicit no-duplication rule.
- Dialog coverage needed to include more than the ISO/USB progress dialog.
- Start validation needed a concrete readiness issue model, not just command booleans.

### Locked corrections
- Keep one GitHub release per Foundry version. It must carry:
  - Foundry Velopack desktop assets,
  - `Foundry.Connect-win-x64.zip`,
  - `Foundry.Connect-win-arm64.zip`,
  - `Foundry.Deploy-win-x64.zip`,
  - `Foundry.Deploy-win-arm64.zip`.
- Build, package, and validate all release assets before publishing the GitHub release.
- Use Velopack `packId` `FoundryOSD.Foundry`.
- Use MSI `--instLocation PerMachine`.
- Publish Foundry self-contained, unpackaged, and non-single-file.
- Use Velopack channels `win-x64-stable` and `win-arm64-stable`.
- Allow deltas only after implementation proves the GitHub Releases + multi-architecture channel setup works safely; otherwise disable deltas for first rollout.
- Keep signing out of scope for this migration plan.
- Make Home read-only, Configuration editable, and Start review/action-only.
- Use Settings for app language only.
- Use Settings only for update checks. About is informational.
- Disable update install actions gracefully when Foundry is not Velopack-installed.
- Navigate to Start after importing configuration.
- Remove the persistent shell footer/status area in favor of Home/Start/dialog status.
- Use a central readiness issue model for Start aggregation.
- Use ContentDialog for app-owned dialogs and HWND-initialized OS pickers for file/folder selection.

## Implementation Readiness and Publish Validation Closure - 2026-04-29

### Second-audit closure decisions
- Preserve Foundry's administrator execution requirement during the WinUI conversion. The current `src\Foundry\app.manifest` requests `requireAdministrator`; the WinUI project conversion must not drop that manifest or equivalent elevation behavior.
- ADK navigation gate:
  - until compatible ADK/WinPE Add-on state is confirmed, only `Home`, `ADK`, `Settings`, and `About` are accessible;
  - `Configuration`, `Start`, `Network`, `Localization`, `Autopilot`, and `Customization` remain visible but disabled in NavigationView;
  - disabled items should expose a short ADK-required explanation through tooltip/status text.
- Status surface matrix:
  - no footer/status bar;
  - ISO/USB progress and terminal result live in the locked operation dialog;
  - ADK install/upgrade/status progress lives inline on the ADK page;
  - Autopilot import/download progress lives inline on the Autopilot page;
  - configuration import/export success/failure appears as page-level InfoBars on Start;
  - USB count, version, and readiness summaries belong on Home/Start or Settings/About, not in a shell footer.
- Logs behavior:
  - remove `Logs` from NavigationView footer;
  - expose logs as a read-only Settings card/action that opens the logs folder.
- About behavior:
  - keep About as a NavigationView footer action that opens an informational ContentDialog;
  - keep version, license, authors, support, and project links;
  - remove update checks from About.
- Autopilot tenant profile picker:
  - keep the task modal for first migration;
  - implement as a large scrolling ContentDialog;
  - preserve table-style selection, multi-select, Ctrl+A, and import footer actions.
- Start configuration actions:
  - expose only the three existing actions: import expert config, export expert config, and export deploy config;
  - do not add deploy-config import;
  - importing expert config updates Configuration and expert pages, does not change app language, and navigates to Start.
- Deploy config export:
  - always export from the full current configuration after Standard/Expert mode removal;
  - export is available even when the expert pages are at safe defaults.
- Settings paths:
  - logs, cache, and temp locations are read-only cards with open-folder actions for the first migration;
  - do not add editable path preferences.
- Readiness rules:
  - keep current hard blockers close to today's behavior: incompatible/missing ADK, network validation failure, missing WinPE language, missing ISO path for ISO creation, and missing USB disk for USB creation;
  - add warnings, not blockers, for Autopilot enabled with zero profiles, incomplete machine-naming inputs, zero visible deployment languages, missing/unreadable custom driver path, and ISO output path parent missing or likely not writable;
  - Start summary must not imply unsupported customization features that are currently placeholders.

### Connect/Deploy runtime-layout clarification
- Connect and Deploy release asset names remain unchanged, but their runtime contracts are not identical.
- Connect is provisioned/extracted into the WinPE runtime tree and should be validated as runtime content after media creation.
- Deploy remains available through the embedded archive fallback path and should be validated both as a release-fed archive and embedded fallback.
- Local override behavior must be validated separately:
  - explicit archive environment variables from the local WinPE script path;
  - transient project publish and zip generation inside Foundry's WinPE local embedding services;
  - manual publish scripts that output folders rather than zips.

### Deep Foundry publish validation
- Test Foundry publish behavior independently before Velopack packaging:
  - `win-x64` and `win-arm64`;
  - Release configuration;
  - self-contained;
  - unpackaged;
  - non-single-file;
  - clean output directory per run;
  - repeat publish twice to catch stale artifact issues.
- Validate publish output contents:
  - `Foundry.exe` exists and starts;
  - Windows App SDK / WinUI runtime dependencies are present as expected;
  - app manifest still requests administrator elevation;
  - assets, icons, resources, `.resx` satellite assemblies, 7z assets, WinPE assets, and configuration assets are present;
  - no WPF-only Foundry assemblies/resources remain unintentionally.
- Validate runtime behavior directly from the publish folder:
  - app starts elevated;
  - logs are created;
  - Settings paths resolve correctly;
  - non-Velopack update state is handled gracefully;
  - ADK detection works;
  - Home/ADK-only navigation gate works before compatible ADK state;
  - no missing resource/theme/localization failures.
- Validate Velopack input/output:
  - Velopack packs from the publish folder, not from build output;
  - `--mainExe Foundry.exe`;
  - `--packId FoundryOSD.Foundry`;
  - `--instLocation PerMachine`;
  - correct `win-x64-stable` and `win-arm64-stable` channels;
  - MSI, setup/update assets, release indexes, and package files are produced as expected.
- Validate installed behavior:
  - MSI installs to the expected per-machine location;
  - execution stub/current folder layout is correct;
  - app starts elevated after install;
  - update checks work only when installed through Velopack;
  - uninstall removes the app cleanly without deleting shared Foundry workspaces unless explicitly intended.
- Validate publish/release failure cases:
  - missing publish output fails the workflow;
  - missing Velopack assets fails the workflow;
  - missing Connect/Deploy zips fails the workflow;
  - release is not published until all Foundry and WinPE artifacts pass validation.

## Final Coherence Pass and Proof Gates - 2026-04-29

### Final coherence corrections
- Normalized the canonical Logs placement:
  - `Logs` is not a `NavigationView` footer item;
  - `Logs` is exposed from `Settings` as a read-only open-folder action.
- Normalized the Settings language wording:
  - `Settings` owns app UI language only;
  - `Configuration` owns WinPE language;
  - `Localization` owns deployment languages and time zone.
- Removed the stale "likely publish" wording. Foundry publish is locked as self-contained, unpackaged, non-single-file, runtime-specific output for `win-x64` and `win-arm64`.

### Additional implementation proof gates
- Velopack elevated per-machine proof:
  - the plan keeps `requireAdministrator` and `PerMachine`;
  - implementation must prove install, app launch, update, rollback/failure behavior, and uninstall on clean x64 and ARM64 hosts;
  - if elevated per-machine self-update is unreliable, implementation must stop and choose a fallback before release.
- Windows App SDK / Visual C++ runtime proof:
  - self-contained .NET output does not automatically remove every native runtime concern;
  - implementation must decide whether Velopack bootstraps Visual C++ Redistributable or the project declares it as an environment prerequisite;
  - this must be validated during publish/install testing.
- Velopack startup hook:
  - Foundry already uses a custom entrypoint;
  - the future WinUI entrypoint must run Velopack app hooks as early as possible before normal host/UI startup so install, update, uninstall, and obsolete hooks can execute correctly.
- Velopack channel selection:
  - package channels remain `win-x64-stable` and `win-arm64-stable`;
  - runtime update code should prefer the installed package's channel/feed behavior instead of hard-coding a channel, unless implementation proves explicit channel selection is required.
- Windows Community Toolkit Settings controls:
  - `CommunityToolkit.WinUI.Controls.SettingsControls` remains the approved first Toolkit UI package;
  - implementation must verify package version compatibility with the selected Windows App SDK and whether Toolkit resource dictionaries must be merged for correct rendering.
- Debug local WinPE override versus release-fed paths:
  - Foundry's current debug startup can auto-enable local Connect/Deploy project paths when a debugger is attached;
  - implementation must test debugger-attached local override behavior separately from non-debug/no-override behavior that exercises release-fed GitHub artifact contracts.
- Connect/Deploy publish regression after MSBuild split:
  - Connect and Deploy depend on WPF build properties currently inherited globally;
  - after making Foundry WinUI-specific and scoping WPF to WPF projects, implementation must publish Connect and Deploy through the release workflow, manual scripts, and local embedding services before moving on.

## Synthesis

### Current architecture
- `Foundry`, `Foundry.Connect`, and `Foundry.Deploy` are WPF `WinExe` applications on `net10.0-windows` through `src\Directory.Build.props`.
- All three applications share the same hosting pattern: custom `Program.Main`, `Microsoft.Extensions.Hosting`, DI-registered `App` and `MainWindow`, manual `App.InitializeComponent()`, and WPF `app.Run(window)`.
- `Foundry` is the media-authoring application. Its core business logic builds expert/connect/deploy configuration, prepares WinPE media, embeds Connect/Deploy runtimes, manages ADK status, and publishes ISO/USB outputs.
- `Foundry.Connect` and `Foundry.Deploy` are runtime applications intended for WinPE. They should stay WPF and keep their single-file self-contained publish model.

### WPF-specific surface
- Global WPF build flag: `UseWPF=true` in `src\Directory.Build.props`.
- Foundry app bootstrap: WPF dispatcher exception handling and WPF `Application`/`Window` lifecycle in `Program.cs`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, and `MainWindow.xaml.cs`.
- UI composition: WPF `Window`, `Menu`, `DataTemplate`, `ContentControl`, `ListBox`, `ListView`, `GridViewColumn`, `Style.Triggers`, `DataTrigger`, `MultiBinding`, `DynamicResource`, `BooleanToVisibilityConverter`, and `PresentationFramework.Fluent`.
- Shell service: WPF windows, WPF message boxes, `Microsoft.Win32` dialogs, `OpenFolderDialog`, modal `ShowDialog`, owner resolution through `Application.Current.Windows`, and WPF dispatcher invocation.
- Threading: WPF `Dispatcher` in viewmodels and operation progress service.
- Theme: WPF resource dictionary clearing and WPF pack URIs.
- Release notes: WPF `FlowDocument` and document inline model.

### Cross-project impact
- No app-to-app project references exist, but source and runtime contracts do exist.
- `Foundry.Deploy` links `..\Foundry\Assets\7z\**\*`, so moving or restructuring Foundry assets can break Deploy.
- Foundry embeds or downloads `Foundry.Connect` and `Foundry.Deploy` releases into WinPE media through fixed runtime folder and archive conventions.
- Connect/Deploy share the WPF host/theme pattern and depend on global `UseWPF=true`; any global build change must preserve their WPF status explicitly.

### Build / publish / workflow impact
- CI builds the whole solution across `x64` and `ARM64`.
- Release publishing currently assumes Foundry can be published as a single self-contained file and copied as `Foundry-x64.exe` / `Foundry-arm64.exe`.
- Current Microsoft guidance says unpackaged WinUI 3 apps cannot produce a single-file EXE. This is why Foundry moves to Velopack MSI and update assets.
- Foundry release assets become Velopack outputs generated from self-contained, non-single-file publish folders for `win-x64` and `win-arm64`.
- MSIX is rejected and should not be reintroduced as a target.
- Connect/Deploy release assets remain the current WPF zip artifacts and must be present in every public release because WinPE bootstrap depends on GitHub `latest` and fixed zip names.
- The release workflow should publish the GitHub release only after all Velopack and WinPE assets are built and validated.
- The implementation should add a non-release validation path for Foundry publish + Velopack pack before relying on the release workflow.
- README/download badges must be updated from the old direct `.exe` assets to the selected Velopack MSI/download assets.

### Keep / adapt / redesign / replace / remove
- Keep: models, configuration services, WinPE services, ADK services, Autopilot service logic, localization strings content, logging, update HTTP parsing, tests around business logic.
- Adapt: viewmodels, `IApplicationShellService`, `IThemeService`, operation progress events, localization binding wrapper, startup update flow.
- Redesign: main WinUI shell, menus/commands, settings layout, dialog ownership, release-notes rendering, theme handling, window sizing/activation, publish asset strategy.
- Replace: WPF XAML, WPF windows/dialogs, WPF dispatcher usage, WPF resource dictionaries, WPF converters, `FlowDocument` builder, `MessageBoxImage` in public service contracts.
- Remove: WPF `ApplicationDefinition`/`Page Include=App.xaml` workaround from Foundry, `PresentationFramework.Fluent` dependency from Foundry, and any Foundry-only WPF theme pack URI use.

### WinUI 3 recommendations
- Make UI framework selection project-local or conditional; avoid unconditional `UseWPF=true`.
- Use `UseWinUI=true` and a Windows App SDK package in Foundry only.
- Use unpackaged self-contained WinUI 3 for Foundry and package it with Velopack MSI; do not expect a single-file executable.
- Introduce a small UI dispatcher abstraction so viewmodels do not directly reference WPF or WinUI dispatchers.
- Keep shell operations behind interfaces, but remove WPF types from contracts.
- Use WinUI `ContentDialog` with `XamlRoot`, HWND-initialized pickers where needed, and `DispatcherQueue` for UI marshaling.
- Replace WPF dynamic theme dictionary swapping with WinUI `RequestedTheme`, `ThemeResource`, and `ResourceDictionary.ThemeDictionaries`.
- Replace `FlowDocumentScrollViewer` with a WinUI read-only rendering approach, likely `RichTextBlock` or a controlled Markdown-to-UI adapter.

### Migration strategy
1. Planning validation: decide packaging model and artifact contract first.
2. Build topology prep: scope WPF/WinUI properties per project while leaving Connect/Deploy WPF.
3. Abstraction prep: remove WPF types from Foundry shell/dispatcher/theme contracts without changing behavior yet.
4. WinUI shell skeleton: create Foundry WinUI project structure and restore host/DI/lifetime.
5. Port views and dialogs by feature slice, not by mechanical XAML conversion.
6. Restore WinPE media workflows and validate ISO/USB behavior.
7. Rework release pipeline for chosen Foundry packaging model while preserving Connect/Deploy assets.
8. Final validation: CI matrix, deep publish validation, Velopack pack/install/update checks, release artifact checks, and manual smoke tests on x64/ARM64.

### Risks and open questions
- Highest risk: release topology. A broken or desktop-only latest release can break both Foundry desktop updates and WinPE bootstrap/runtime downloads.
- High risk: global `UseWPF=true` will conflict with a mixed UI solution unless scoped.
- High risk: destructive USB flow depends on confirmation behavior and must be preserved.
- High risk: Velopack GitHub delta behavior with both x64 and ARM64 in one public release must be proven before deltas are enabled.
- Medium risk: theme resources are heavily tied to `PresentationFramework.Fluent`.
- Medium risk: release notes rich text has no direct WPF-to-WinUI equivalent.
- Medium risk: always writing the full deploy configuration changes the former Standard/Expert runtime contract and must be validated against Deploy behavior.
- Remaining open question: whether Velopack deltas are safe with the chosen single-release, multi-architecture GitHub layout. If not, disable deltas for the first rollout.

### Decisions requiring validation
- Before implementation begins, validate the updated plan artifacts as the source of truth.
- During implementation, prove or disable Velopack deltas for the multi-architecture GitHub release layout.
- During implementation, decide whether any non-UI extraction is required only if the migration reveals unavoidable coupling; do not introduce a shared library preemptively.

## Implementation Findings - WinUI Startup and Packaging

### Velopack beta package line
- NuGet flat-container check on 2026-04-29 confirms the latest prerelease for `Velopack` and `vpk` is `0.0.1589-ga2c5a97`.
- `Velopack.Build` latest prerelease is `0.0.1369-g1d5c984`.
- Context7 Velopack docs continue to show the relevant packaging shape: publish to a folder, then run `vpk pack --packId ... --packVersion ... --packDir ... --mainExe ...`, with Windows MSI generation enabled by `--msi`.

### Windows App SDK runtime strategy
- `Microsoft.WindowsAppSDK` `1.8.260416003` builds on the current .NET 10 SDK.
- `Microsoft.WindowsAppSDK` `1.7.260224002` is not currently viable with the `dotnet build` path because its PRI generation targets try to load `Microsoft.Build.Packaging.Pri.Tasks.dll` from `C:\Program Files\dotnet\sdk\10.0.203\Microsoft\VisualStudio\v18.0\AppxPackage`, where the task is not present.
- `WindowsAppSDKSelfContained=true` currently crashes before the WinUI `Application.Start` callback on this machine with native `0xc000027b` / WER `80040154`.
- `WindowsAppSDKSelfContained=false` reaches managed startup and can launch the WinUI shell after adding the standard WinUI resources.
- This means the implementation should not keep assuming "self-contained" means Windows App SDK self-contained. The practical current path is:
  - publish Foundry as a non-single-file folder for Velopack;
  - keep the .NET/runtime publish strategy explicit during publish validation;
  - use Windows App SDK framework runtime/bootstrap behavior unless later proof shows self-contained Windows App SDK output is stable.

### WinUI shell startup requirements
- A custom WinUI `Main` still needs to follow the generated startup shape:
  - Velopack hook first;
  - Windows App Runtime available before WinUI APIs;
  - `WinRT.ComWrappersSupport.InitializeComWrappers()`;
  - `DispatcherQueueSynchronizationContext` inside `Application.Start`;
  - `App.InitializeComponent()` from the `App` constructor.
- `App.xaml` must merge `XamlControlsResources`; otherwise standard WinUI controls such as `NavigationViewItem` fail to resolve theme resources like `TabViewButtonBackground`.

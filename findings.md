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
- Worktree HEAD matches `main` at `46682845972ad677642cef7d986ad8e82b12a65e`.

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
- Current Microsoft guidance says unpackaged WinUI 3 apps cannot produce a single-file EXE. This is the largest operational mismatch.
- If Foundry becomes unpackaged WinUI 3, release assets probably need to become zipped folders or self-contained Windows App SDK folders, not single `.exe` files.
- If Foundry becomes MSIX packaged, current top-level `.exe` download links, release assets, and "no installer" positioning must be reconsidered.
- Connect/Deploy release assets should remain as current WPF zip artifacts unless explicitly changed later.

### Keep / adapt / redesign / replace / remove
- Keep: models, configuration services, WinPE services, ADK services, Autopilot service logic, localization strings content, logging, update HTTP parsing, tests around business logic.
- Adapt: viewmodels, `IApplicationShellService`, `IThemeService`, operation progress events, localization binding wrapper, startup update flow.
- Redesign: main WinUI shell, menus/commands, settings layout, dialog ownership, release-notes rendering, theme handling, window sizing/activation, publish asset strategy.
- Replace: WPF XAML, WPF windows/dialogs, WPF dispatcher usage, WPF resource dictionaries, WPF converters, `FlowDocument` builder, `MessageBoxImage` in public service contracts.
- Remove: WPF `ApplicationDefinition`/`Page Include=App.xaml` workaround from Foundry, `PresentationFramework.Fluent` dependency from Foundry, and any Foundry-only WPF theme pack URI use.

### WinUI 3 recommendations
- Make UI framework selection project-local or conditional; avoid unconditional `UseWPF=true`.
- Use `UseWinUI=true` and a Windows App SDK package in Foundry only.
- Prefer unpackaged/self-contained WinUI 3 only if a folder or zip release is acceptable; do not expect a single-file executable.
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
8. Final validation: CI matrix, local publish, release artifact checks, and manual smoke tests on x64/ARM64.

### Risks and open questions
- Highest risk: current release contract for Foundry is a single `.exe`; WinUI 3 unpackaged publish does not support that.
- High risk: global `UseWPF=true` will conflict with a mixed UI solution unless scoped.
- High risk: destructive USB flow depends on confirmation behavior and must be preserved.
- Medium risk: theme resources are heavily tied to `PresentationFramework.Fluent`.
- Medium risk: release notes rich text has no direct WPF-to-WinUI equivalent.
- Unknown: exact packaging decision for Foundry: unpackaged zip, self-contained folder zip, MSIX, or installer.
- Unknown: whether README/download expectations may change from executable downloads to archive/installer downloads.

### Decisions requiring validation
- Choose Foundry distribution model after migration.
- Decide whether release artifact names may change.
- Decide whether Foundry may stop being a single-file executable.
- Decide whether to keep `.resx` localization for Foundry or migrate to `.resw`/ResourceLoader.
- Decide whether to introduce a small shared non-UI library later, or keep reuse inside the Foundry project for now.
- Decide how much UI redesign is acceptable versus a close visual port.

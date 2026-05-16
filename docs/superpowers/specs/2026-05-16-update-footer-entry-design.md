# Update Footer Entry Design

## Context

Foundry OSD currently shows a shell-level update `InfoBar` in `MainWindow.xaml` when `IApplicationUpdateStateService` publishes an available update. The action button routes to `AppUpdateSettingPage`. Footer navigation items are currently injected in `MainWindow.xaml.cs` for Documentation and About.

## Goals

- Replace the shell-level update `InfoBar` with a footer navigation item.
- Show the footer item only when an update is available.
- Make the available-update state visible and actionable.
- Route the footer item to the existing update settings page.
- Keep the update settings page layout mostly intact for this PR, with two focused improvements:
  - Rename `Download and restart` to `Install and restart`.
  - Show release notes in an About-style `ContentDialog` containing WebView2 and a top-right close button.

## Non-Goals

- Do not redesign the full update settings page in this PR.
- Do not change update check, download, or Velopack behavior.
- Do not add new dependencies.
- Do not change WPF runtime apps.

## Footer Update Indicator

Use the existing runtime footer item pattern in `MainWindow.xaml.cs`.

When `ApplicationUpdateCheckResult.IsUpdateAvailable` is true:

- Add or update a `NavigationViewItem` in `NavView.FooterMenuItems`.
- Place it before Documentation and About so it is easy to see.
- Use glyph `&#xEBD3;` for the footer item icon.
- Use localized text from the existing update strings:
  - visible label: `Update available`
  - tooltip: include the available version when present.
- Add a DevWinUI-style `InfoBadge` so the footer entry is visually prominent.
  - Follow the DevWinUI JsonNavigationService badge model: `NavigationViewInfoBadgeStyle` + `InfoBadgeValue`.
  - Use the `StringInfoBadgeStyle` equivalent for the NavigationView badge.
  - Set the badge value to the available update version when present.
  - Fall back to a short localized update marker when no version is available.
- On click or keyboard activation, navigate to `AppUpdateSettingPage` using the same route and localized title currently used by the `InfoBar` action.

When no update is available:

- Remove the footer item from `NavView.FooterMenuItems`.
- Keep the existing settings page route available from Settings.

Navigation guard behavior remains unchanged:

- Footer items are enabled when the shell is ready.
- Footer items are disabled during a running operation.
- Footer items remain available during ADK-blocked state, consistent with existing footer behavior.

## View Model Changes

Replace update-banner-focused shell state in `MainViewModel` with footer-focused state:

- `IsUpdateFooterItemVisible`
- `UpdateFooterTitle`
- `UpdateFooterToolTip`

The view model still subscribes to `IApplicationUpdateStateService.StateChanged` and refreshes localized strings on language changes. Dismissal state is no longer needed because the item is not closable; it remains visible until the update state changes.

## Main Window Changes

Remove the `UpdateInfoBar` from `MainWindow.xaml`.

In `MainWindow.xaml.cs`:

- Remove `UpdateInfoBar_Closed` and `UpdateInfoBarActionButton_Click`.
- Add `EnsureUpdateFooterItem`.
- Add `RemoveUpdateFooterItem`.
- Add click and keyboard handlers for the update footer item.
- Refresh the update footer item after:
  - navigation initialization
  - localization refresh
  - update state changes through `MainViewModel` bindings or explicit shell refresh
  - shell navigation state changes

The implementation should keep code-behind limited to UI-specific footer item creation and navigation.

## Update Settings Page Changes

Keep the current page structure.

Change the localized button text:

- English: `Install and restart`
- French: `Installer et redémarrer`

Release notes behavior:

- Replace the plain text message dialog used by `GetReleaseNotesCommand`.
- Add an update release notes dialog that follows the existing `AboutDialog` chrome pattern:
  - `ContentDialog`
  - custom top-right close button
  - WebView2 content
  - loading state
  - error state with a fallback link
- Reuse the existing WebView2 user data directory constant.
- Load the same GitHub releases page used by About -> Release notes:
  - display text: `foundry-osd/foundry`
  - URL: `https://github.com/foundry-osd/foundry/releases`
- Ignore Velopack-provided release-note content for this dialog.

## Localization

Update both `en-US` and `fr-FR` resource files for:

- `AppUpdate_DownloadRestartButton.Content`
- any new release-notes dialog loading, error, close, and fallback text if existing About strings cannot be reused cleanly.

Existing update strings should be reused where they fit.

## Testing

Run:

```powershell
dotnet test src\Foundry.slnx
```

No UI tests are required by default because the change is WinUI view/code-behind behavior. Manual verification should cover:

- no footer item when no update is available
- footer item visible when update is available
- footer item navigates to `AppUpdateSettingPage`
- footer item disables during operation-running state
- `Install and restart` text is shown
- release notes open in the WebView2 dialog and close from the top-right button
